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
        
        SubscribeNetworkEvent<PdaPopoutState>(OnPdaPopout);
    }
    
    private void OnPdaPopout(PdaPopoutState state, EntitySessionEventArgs args)
    {
        // Find the PDA BUI
        if (!TryGetPdaBui(args.SenderSession.AttachedEntity, out var bui))
            return;
        
        // Get the menu from the BUI
        var menu = bui.GetMenu();
        if (menu == null)
        {
            Log.Error("PDA menu not found when trying to create popout");
            return;
        }
        
        // If we already have a popout window, close it
        ClosePopout();
        
        // Create a new popout window
        CreatePopout(menu);
    }
    
    // This method is called directly from PdaBoundUserInterface when it receives a PdaPopoutState
    public void CreatePopoutFromBui(PdaBoundUserInterface bui)
    {
        var menu = bui.GetMenu();
        if (menu == null)
        {
            Log.Error("PDA menu not found when trying to create popout from BUI");
            return;
        }
        
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
}
