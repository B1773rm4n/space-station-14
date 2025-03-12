using Content.Shared.PDA;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using System;
using System.Linq;

namespace Content.Client.PDA;

public sealed class PdaSystem : SharedPdaSystem
{
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IUserInterfaceManager _uiManager = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    
    // <summary>
    // Starlight-start: PDA Popout
    // This is a beta version of the PDA popout system.
    // It pops out a window from the original game window and creates a new OS window.
    // </summary>

    /* 
        The problem with this implementation is that there are 3-4 ways to close a window.
        It's easy to pop out the PDA. But to keep track of the popout and make sure that it
        behaves consistent and is always closed,
        no matter from what way of closing you chose is very hard.

        According to Rinary, this needs a proper implementation including client/server communication.
        This is planned to do in a different interation.
        In the mean while this is released in a semi-buggy state.

        Opening the popout usually works very well.
        Closing can be an issue. A user will find out that
        they have to close the OS window for the best result.

        Known issues:
        - The popout window remains open and black when the PDA is closed.
            Generally the popout window has to be closed manually on the OS window frame.
        - A second invocation of "Toggle UI" on the PDA context menu will not work and
        have to be triggered another time
        - Not introduced but became with this feature more obvious :
            The Crew Monitor doesn't update by itself. So even if it's not popped out,
            it have to be closed and reopened to get the latest data. I would advice a
            running timer when the Crew Monitor is open which updates every 5 seconds.
    */
    
    private readonly ISawmill _sawmill = Logger.GetSawmill("PdaSystem");

    private IClydeWindow? ClydeWindow;
    private WindowRoot? WindowRoot;
    
    // Track if the window is currently open to avoid multiple close/open operations
    private bool _isPopoutOpen;
    
    // Track if we're currently in the process of closing the window
    private bool _isClosingPopout;
    
    public override void Initialize()
    {
        base.Initialize();
                
        SubscribeNetworkEvent<PdaPopoutState>(OnPdaPopout);
    }
    
    private void OnPdaPopout(PdaPopoutState state, EntitySessionEventArgs args)
    {
        try
        {
            // If we're already closing, don't try to open a new window
            if (_isClosingPopout)
            {
                Log.Warning("Attempted to open PDA popout while closing another one");
                return;
            }
            
            // Find the PDA BUI
            if (!TryGetPdaBui(args.SenderSession.AttachedEntity, out var bui))
            {
                Log.Warning("Could not find PDA BUI for popout");
                return;
            }
            
            // Toggle the popout through the BUI
            bui?.TogglePopout();
        }
        catch (Exception ex)
        {
            Log.Error($"Error in OnPdaPopout: {ex}");
        }
    }
    
    private bool TryGetPdaBui(EntityUid? uid, out PdaBoundUserInterface? bui)
    {
        bui = null;
        
        if (uid == null)
            return false;
        
        foreach (var uiComp in EntityManager.GetComponents<UserInterfaceComponent>(uid.Value))
        {
            foreach (var ui in uiComp.ClientOpenInterfaces.Values)
            {
                if (ui is PdaBoundUserInterface pdaBui)
                {
                    bui = pdaBui;
                    return true;
                }
            }
        }
        
        return false;
    }
    
    // This method is called from PdaBoundUserInterface when it's disposed
    public void ClosePopoutIfOpen()
    {
        try
        {
            // Check if we have any popout resources that need to be cleaned up
            if (_isPopoutOpen || ClydeWindow != null || WindowRoot != null)
            {
                Log.Debug("Closing PDA popout window from ClosePopoutIfOpen");
                ClosePopout();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error in ClosePopoutIfOpen: {ex}");
            
            // Force cleanup in case of error
            ClydeWindow = null;
            WindowRoot = null;
            _isPopoutOpen = false;
            _isClosingPopout = false;
        }
    }
    
    // This method should be called during frame updates to check if the window is still valid

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        
        // Check if the window has been closed externally
        if (_isPopoutOpen && (ClydeWindow == null || ClydeWindow.IsDisposed))
        {
            Log.Debug("Detected externally closed PDA popout window");
            ClosePopout();
        }
    }
    
    // Public method to check if the popout is currently open
    public bool IsPopoutOpen()
    {
        // Check both our internal state flag and the actual window state
        return _isPopoutOpen && ClydeWindow != null && !ClydeWindow.IsDisposed;
    }
    
    // Creates a popout window for the PDA menu
    public void CreatePopout(PdaMenu menu)
    {
        // If the window is already open, just close it (toggle behavior)
        if (IsPopoutOpen())
        {
            Log.Debug("PDA popout window already open, closing it (toggle behavior)");
            ClosePopout();
            return;
        }
        
        // Make absolutely sure any existing popout is closed first
        ClosePopout();
        
        try
        {
            // Set flag to indicate we're opening a window
            _isPopoutOpen = true;
            
            // Orphan the menu from its current parent
            menu.Orphan();
            
            // Subscribe to the window's close event
            menu.OnPdaWindowClosed += ClosePopoutIfOpen;
            
            // Get the second monitor as the primary monitor is the game window
            var monitor = _clyde.EnumerateMonitors().Skip(1).FirstOrDefault();

            // Create a new window        
            ClydeWindow = _clyde.CreateWindow(new WindowCreateParameters
            {
                Maximized = false,
                Title = "PDA",
                Monitor = monitor,
                Width = 576,
                Height = 450
            });
            
            ClydeWindow.RequestClosed += OnWindowClosed;
            ClydeWindow.DisposeOnClose = true;
            
            // Create a window root and add the menu to it
            WindowRoot = _uiManager.CreateWindowRoot(ClydeWindow);
            WindowRoot.AddChild(menu);
            
            // Disable the popout button in the popout window
            menu.PopoutButton.Disabled = true;
            menu.PopoutButton.Visible = false;
            
            Log.Debug("PDA popout window created successfully");
        }
        catch (Exception ex)
        {
            Log.Error($"Error creating PDA popout window: {ex}");
            ClosePopout(); // Ensure cleanup if an error occurs
        }
    }
    
    private void OnWindowClosed(WindowRequestClosedEventArgs args)
    {
        try
        {
            Log.Debug("PDA popout window close requested");
            ClosePopout();
        }
        catch (Exception ex)
        {
            Log.Error($"Error in OnWindowClosed: {ex}");
        }
    }
    
    private void ClosePopout()

    {
        Log.Debug("Start try in ClosePopout");

        // If we're already in the process of closing, don't try again
        if (_isClosingPopout)
            return;
            
        // If there's nothing to close, just return
        if (!_isPopoutOpen && ClydeWindow == null && WindowRoot == null)
            return;
            
        // Set flag to indicate we're closing
        _isClosingPopout = true;
        
        try
        {
            Log.Debug("Closing PDA popout window");
            
            if (WindowRoot != null)
            {
                // Remove all children from the window root
                WindowRoot.RemoveAllChildren();
                WindowRoot = null;
            }
            
            if (ClydeWindow != null)
            {
                // Check if the window is already disposed
                if (!ClydeWindow.IsDisposed)
                {
                    // Unsubscribe from events
                    ClydeWindow.RequestClosed -= OnWindowClosed;
                    
                    // Dispose the window
                    ClydeWindow.Dispose();
                }
                ClydeWindow = null;
            }
            
            // Reset the open flag
            _isPopoutOpen = false;
            
            Log.Debug("PDA popout window closed successfully");
        }
        catch (Exception ex)
        {
            Log.Error($"Error closing PDA popout window: {ex}");
        }
        finally
        {
            // Reset all references to ensure we don't have dangling references
            ClydeWindow = null;
            WindowRoot = null;
            _isPopoutOpen = false;
            _isClosingPopout = false;
        }
    }
    // Starlight-end
}
