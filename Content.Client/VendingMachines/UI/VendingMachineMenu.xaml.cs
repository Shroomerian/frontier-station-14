using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared.VendingMachines;
using Content.Shared.Cargo.Components;
using Content.Shared.Stacks;
using Robust.Client.AutoGenerated;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Prototypes;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.Reagent;
using FancyWindow = Content.Client.UserInterface.Controls.FancyWindow;
using Content.Shared.IdentityManagement;
using Robust.Shared.Timing;

namespace Content.Client.VendingMachines.UI
{
    [GenerateTypedNameReferences]
    public sealed partial class VendingMachineMenu : FancyWindow
    {
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IGameTiming _timing = default!;

        private readonly Dictionary<EntProtoId, EntityUid> _dummies = [];

        public event Action<ItemList.ItemListSelectedEventArgs>? OnItemSelected;
        public event Action<string>? OnSearchChanged;

        public VendingMachineMenu()
        {
            MinSize = SetSize = new Vector2(250, 150);
            RobustXamlLoader.Load(this);
            IoCManager.InjectDependencies(this);

            SearchBar.OnTextChanged += _ =>
            {
                OnSearchChanged?.Invoke(SearchBar.Text);
            };

            VendingContents.OnItemSelected += args =>
            {
                OnItemSelected?.Invoke(args);
            };
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            // Don't clean up dummies during disposal or we'll just have to spawn them again
            if (!disposing)
                return;

            // Delete any dummy items we spawned
            foreach (var entity in _dummies.Values)
            {
                _entityManager.QueueDeleteEntity(entity);
            }
            _dummies.Clear();
        }

        /// <summary>
        /// Populates the list of available items on the vending machine interface
        /// and sets icons based on their prototypes
        /// </summary>
        public void Populate(List<VendingMachineInventoryEntry> inventory, float priceModifier, out List<int> filteredInventory,  string? filter = null)
        {
            filteredInventory = new();

            if (inventory.Count == 0)
            {
                VendingContents.Clear();
                var outOfStockText = Loc.GetString("vending-machine-component-try-eject-out-of-stock");
                VendingContents.AddItem(outOfStockText);
                SetSizeAfterUpdate(outOfStockText.Length, VendingContents.Count);
                return;
            }

            while (inventory.Count != VendingContents.Count)
            {
                if (inventory.Count > VendingContents.Count)
                    VendingContents.AddItem(string.Empty);
                else
                    VendingContents.RemoveAt(VendingContents.Count - 1);
            }

            var longestEntry = string.Empty;
            var spriteSystem = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<SpriteSystem>();

            var filterCount = 0;
            for (var i = 0; i < inventory.Count; i++)
            {
                var entry = inventory[i];
                var vendingItem = VendingContents[i - filterCount];
                vendingItem.Text = string.Empty;
                vendingItem.Icon = null;

                if (!_dummies.TryGetValue(entry.ID, out var dummy))
                {
                    dummy = _entityManager.Spawn(entry.ID);
                    _dummies.Add(entry.ID, dummy);
                }

                var itemName = Identity.Name(dummy, _entityManager);
                Texture? icon = null;
                if (_prototypeManager.TryIndex<EntityPrototype>(entry.ID, out var prototype))
                {
                    icon = spriteSystem.GetPrototypeIcon(prototype).Default;
                }

                // search filter
                if (!string.IsNullOrEmpty(filter) &&
                    !itemName.ToLowerInvariant().Contains(filter.Trim().ToLowerInvariant()))
                {
                    VendingContents.Remove(vendingItem);
                    filterCount++;
                    continue;
                }

                if (itemName.Length > longestEntry.Length)
                    longestEntry = itemName;
                // ok so we dont really have access to the pricing system so we are doing a quick price check
                // based on prototype info since the items inside a vending machine dont actually exist as entities
                // until they are spawned. So this little alg does the following:
                // first, checks for a staticprice component, and if it has one, checks to make sure its not 0 since
                // stacks and other items have 0 cost.
                // If the price is 0, then we check for both a stack price and a stack component, since if it has one
                // it should have the other too, and then calculates the price based on that.
                // If the price is still 0 or non-existant (this is the case for food and containers since their value is
                // determined dynamically by their contents/inventory), it then falls back to the default mystery
                // hardcoded value of 20xMarketModifier.
                var cost = 20;
                if (prototype != null && prototype.TryGetComponent<StaticPriceComponent>(out var priceComponent))
                {
                    if (priceComponent.Price != 0)
                    {
                        var price = (float)priceComponent.Price;
                        cost = (int) (price * priceModifier);
                    }
                    else
                    {
                        if (prototype.TryGetComponent<StackPriceComponent>(out var stackPrice) && prototype.TryGetComponent<StackComponent>(out var stack))
                        {
                            var price = stackPrice.Price * stack.Count;
                            cost = (int) (price * priceModifier);
                        }
                        else
                            cost = (int) (cost * priceModifier);
                    }
                }
                else
                    cost = (int) (cost * priceModifier);

                if (prototype != null && prototype.TryGetComponent<SolutionContainerManagerComponent>(out var priceSolutions))
                {
                    if (priceSolutions.Solutions != null)
                    {
                        foreach (var solution in priceSolutions.Solutions.Values)
                        {
                            foreach (var (reagent, quantity) in solution.Contents)
                            {
                                if (!_prototypeManager.TryIndex<ReagentPrototype>(reagent.Prototype,
                                        out var reagentProto))
                                    continue;

                                // TODO check ReagentData for price information?
                                var costReagent = (float) quantity * reagentProto.PricePerUnit;
                                cost += (int) (costReagent * priceModifier);
                            }
                        }
                    }
                }

                // This block exists to allow the VendPrice flag to set a vending machine item price.
                if (prototype != null && prototype.TryGetComponent<StaticPriceComponent>(out var vendPriceComponent) && vendPriceComponent.VendPrice != 0 && cost <= (float) vendPriceComponent.VendPrice)
                {
                    var price = (float) vendPriceComponent.VendPrice;
                    cost = (int) price;
                }
                // This block exists to allow the VendPrice flag to set a vending machine item price.

                vendingItem.Text = $"[${cost}] {itemName} [{entry.Amount}]";
                vendingItem.Icon = icon;
                filteredInventory.Add(i);
            }

            SetSizeAfterUpdate(longestEntry.Length, inventory.Count);
        }

        public void UpdateBalance(int balance)
        {
            BalanceLabel.Text = Loc.GetString("cargo-console-menu-points-amount", ("amount", balance.ToString()));
        }

        private void SetSizeAfterUpdate(int longestEntryLength, int contentCount)
        {
            SetSize = new Vector2(Math.Clamp((longestEntryLength + 2) * 12, 250, 300),
                Math.Clamp(contentCount * 50, 150, 350));
        }
    }
}
