using System.Linq;
using Content.Client.UserInterface.Controls;
using Content.Client.Shipyard.BUI;
using Content.Shared.Shipyard.BUI;
using Content.Shared.Shipyard.Prototypes;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Prototypes;
using static Robust.Client.UserInterface.Controls.BaseButton;
using Content.Shared.Shipyard;

namespace Content.Client.Shipyard.UI;

[GenerateTypedNameReferences]
public sealed partial class ShipyardConsoleMenu : FancyWindow
{
    [Dependency] private readonly IPrototypeManager _protoManager = default!;

    public event Action<ButtonEventArgs>? OnSellShip;
    public event Action<ButtonEventArgs>? OnOrderApproved;
    private readonly ShipyardConsoleBoundUserInterface _menu;
    private readonly List<string> _categoryStrings = new();
    private string? _category;

    private List<string> _lastProtos = new();
    private string _lastType = "";
    private bool _freeListings = false;

    public ShipyardConsoleMenu(ShipyardConsoleBoundUserInterface owner)
    {
        RobustXamlLoader.Load(this);
        IoCManager.InjectDependencies(this);
        _menu = owner;
        Title = Loc.GetString("shipyard-console-menu-title");
        SearchBar.OnTextChanged += OnSearchBarTextChanged;
        Categories.OnItemSelected += OnCategoryItemSelected;
        SellShipButton.OnPressed += (args) => { OnSellShip?.Invoke(args); };
    }


    private void OnCategoryItemSelected(OptionButton.ItemSelectedEventArgs args)
    {
        SetCategoryText(args.Id);
        PopulateProducts(_lastProtos, _lastType, _freeListings);
    }

    private void OnSearchBarTextChanged(LineEdit.LineEditEventArgs args)
    {
        PopulateProducts(_lastProtos, _lastType, _freeListings);
    }

    private void SetCategoryText(int id)
    {
        _category = id == 0 ? null : _categoryStrings[id];
        Categories.SelectId(id);
    }

    /// <summary>
    ///     Populates the list of products that will actually be shown, using the current filters.
    /// </summary>
    public void PopulateProducts(List<string> prototypes, string type, bool free)
    {
        Vessels.RemoveAllChildren();

        var newVessels = prototypes.Select(it => _protoManager.TryIndex<VesselPrototype>(it, out var proto) ? proto : null)
            .Where(it => it != null)
            .ToList();

        newVessels.Sort((x, y) =>
            string.Compare(x!.Name, y!.Name, StringComparison.CurrentCultureIgnoreCase));

        var search = SearchBar.Text.Trim().ToLowerInvariant();

        foreach (var prototype in newVessels)
        {
            // if no search or category
            // else if search
            // else if category and not search
            if (search.Length == 0 && _category == null ||
                search.Length != 0 && prototype!.Name.ToLowerInvariant().Contains(search) ||
                search.Length == 0 && _category != null && prototype!.Category.Equals(_category))
            {
                string priceText;
                if (_freeListings)
                    priceText = Loc.GetString("shipyard-console-menu-listing-free");
                else
                    priceText = Loc.GetString("shipyard-console-menu-listing-amount", ("amount", prototype!.Price.ToString()));

                var vesselEntry = new VesselRow
                {
                    Vessel = prototype,
                    VesselName = { Text = prototype!.Name },
                    Purchase = { ToolTip = prototype.Description, TooltipDelay = 0.2f },
                    Price = { Text = priceText },
                };
                vesselEntry.Purchase.OnPressed += (args) => { OnOrderApproved?.Invoke(args); };
                Vessels.AddChild(vesselEntry);
            }
        }

        _lastProtos = prototypes;
        _lastType = type;
    }

    /// <summary>
    ///     Populates the list categories that will actually be shown, using the current filters.
    /// </summary>
    public void PopulateCategories()
    {
        _categoryStrings.Clear();
        Categories.Clear();
        var vessels = _protoManager.EnumeratePrototypes<VesselPrototype>();
        foreach (var prototype in vessels)
        {
            if (!_categoryStrings.Contains(prototype.Category))
            {
                _categoryStrings.Add(Loc.GetString(prototype.Category));
            }
        }

        _categoryStrings.Sort();

        // Add "All" category at the top of the list
        _categoryStrings.Insert(0, Loc.GetString("cargo-console-menu-populate-categories-all-text"));

        foreach (var str in _categoryStrings)
        {
            Categories.AddItem(str);
        }
    }

    public void UpdateState(ShipyardConsoleInterfaceState state)
    {
        BalanceLabel.Text = Loc.GetString("shipyard-console-menu-listing-amount", ("amount", state.Balance.ToString()));
        int shipPrice = 0;
        if (!state.FreeListings)
            shipPrice = state.ShipSellValue;

        ShipAppraisalLabel.Text = Loc.GetString("shipyard-console-menu-listing-amount", ("amount", shipPrice.ToString()));
        SellShipButton.Disabled = state.ShipDeedTitle == null;
        TargetIdButton.Text = state.IsTargetIdPresent
            ? Loc.GetString("id-card-console-window-eject-button")
            : Loc.GetString("id-card-console-window-insert-button");
        if (state.ShipDeedTitle != null)
        {
            DeedTitle.Text = state.ShipDeedTitle;
        }
        else
        {
            DeedTitle.Text = $"None";
        }
        _freeListings = state.FreeListings;
        PopulateProducts(_lastProtos, _lastType, _freeListings);
    }
}
