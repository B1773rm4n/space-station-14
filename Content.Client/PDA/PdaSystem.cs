using Content.Shared.PDA;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
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
    
    public override void Initialize()
    {
        base.Initialize();
        
        Log = _logManager.GetSawmill("pda");
        
    }

    /* The problem with this implementation is that there are 3-4 ways to close a window.
        It's easy to pop out the PDA. But to keep track of the popout and make sure that it
        behaves consistent and is always closed, no matter from what way of closing you chose is very hard
        As this was not a official requirement anyway, I abandon this idea as it was meant as an exploration
        for the request of AI crew monitor as a separate window   */
    
    public void  OnPdaPopout(PdaMenu menu)
    {

        // If we already have a popout window, close it
        ClosePopout();
        
        // Create a new popout window
        CreatePopout(menu);
    }
        
    private void CreatePopout(PdaMenu menu)
    {
        // Orphan the menu from its current parent
        menu.Orphan();
        
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
    }
    
    private void OnWindowClosed(WindowRequestClosedEventArgs args)
    {
        ClosePopout();
    }
    
    private void ClosePopout()
    {
        if (_popoutMenu == null || ClydeWindow == null || WindowRoot == null)
            return;
        
        // Remove the menu from the window root
        _popoutMenu.Orphan();
        
        // Dispose the window
        ClydeWindow.Dispose();
        ClydeWindow = null;
        WindowRoot = null;
        _popoutMenu = null;
    }
}
