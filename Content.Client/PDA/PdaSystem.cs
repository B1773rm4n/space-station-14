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

    public ISawmill Log { get; private set; } = default!;
    
    private PdaMenu? _popoutMenu;
    private IClydeWindow? ClydeWindow;
    private WindowRoot? WindowRoot;
    
    // Track if the window is currently open to avoid multiple close/open operations
    private bool _isPopoutOpen;
    
    // Track if we're currently in the process of closing the window
    private bool _isClosingPopout;
    
    public override void Initialize()
    {
        base.Initialize();
        
        Log = _logManager.GetSawmill("pda");
        
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
            
            // Get the menu from the BUI
            var menu = bui.GetMenu();
            if (menu == null)
            {
                Log.Error("PDA menu not found when trying to create popout");
                return;
            }
            
            // Create a new popout window (this will close any existing popout)
            CreatePopout(menu);
        }
        catch (Exception ex)
        {
            Log.Error($"Error in OnPdaPopout: {ex}");
            ClosePopout(); // Ensure cleanup if an error occurs
        }
    }
    
    // This method is called directly from PdaBoundUserInterface when it receives a PdaPopoutState
    public void CreatePopoutFromBui(PdaBoundUserInterface bui)
    {
        try
        {
            // If we're already closing, don't try to open a new window
            if (_isClosingPopout)
            {
                Log.Warning("Attempted to open PDA popout from BUI while closing another one");
                return;
            }
            
            var menu = bui.GetMenu();
            if (menu == null)
            {
                Log.Error("PDA menu not found when trying to create popout from BUI");
                return;
            }
            
            // Create a new popout window (this will close any existing popout)
            CreatePopout(menu);
        }
        catch (Exception ex)
        {
            Log.Error($"Error in CreatePopoutFromBui: {ex}");
            ClosePopout(); // Ensure cleanup if an error occurs
        }
    }
        
    private void CreatePopout(PdaMenu menu)
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
            
            // Create a new window
            var monitor = _clyde.EnumerateMonitors().First();
            
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
            
            // Store the menu for later
            _popoutMenu = menu;
            
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
        // If we're already in the process of closing, don't try again
        if (_isClosingPopout)
            return;
            
        // If there's nothing to close, just return
        if (!_isPopoutOpen && _popoutMenu == null && ClydeWindow == null && WindowRoot == null)
            return;
            
        // Set flag to indicate we're closing
        _isClosingPopout = true;
        
        try
        {
            Log.Debug("Closing PDA popout window");
            
            // First, unsubscribe from events to prevent multiple calls
            if (_popoutMenu != null)
            {
                // Unsubscribe from the window's close event
                _popoutMenu.OnPdaWindowClosed -= ClosePopoutIfOpen;
                
                // Remove the menu from the window root if it has a parent
                if (_popoutMenu.Parent != null)
                {
                    _popoutMenu.Orphan();
                }
                
                // Reset the popout button state - IMPORTANT for toggle behavior
                _popoutMenu.PopoutButton.Disabled = false;
                _popoutMenu.PopoutButton.Visible = true;
                _popoutMenu.PopoutButton.Pressed = false;
                
                _popoutMenu = null;
            }
            
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
            _popoutMenu = null;
            ClydeWindow = null;
            WindowRoot = null;
            _isPopoutOpen = false;
            _isClosingPopout = false;
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
            if (_isPopoutOpen || _popoutMenu != null || ClydeWindow != null || WindowRoot != null)
            {
                Log.Debug("Closing PDA popout window from ClosePopoutIfOpen");
                ClosePopout();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error in ClosePopoutIfOpen: {ex}");
            
            // Force cleanup in case of error
            _popoutMenu = null;
            ClydeWindow = null;
            WindowRoot = null;
            _isPopoutOpen = false;
            _isClosingPopout = false;
        }
    }
    
    // This method should be called during frame updates to check if the window is still valid
    public void Update()
    {
        // Check if the window has been closed externally
        if (_isPopoutOpen && (ClydeWindow == null || ClydeWindow.IsDisposed))
        {
            Log.Debug("Detected externally closed PDA popout window");
            ClosePopout();
        }
    }
    
    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        
        // Check window state during frame updates
        Update();
    }
    
    // Public method to check if the popout is currently open
    public bool IsPopoutOpen()
    {
        // Check both our internal state flag and the actual window state
        return _isPopoutOpen && ClydeWindow != null && !ClydeWindow.IsDisposed;
    }
}
