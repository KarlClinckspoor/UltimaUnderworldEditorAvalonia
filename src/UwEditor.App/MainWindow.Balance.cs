using Avalonia.Interactivity;

namespace UwEditor.App
{
    /// <summary>Tools ▸ Balance: the barter simulator and the weapons and armour overviews.</summary>
    public partial class MainWindow
    {
        BarterSimulatorWindow barterWindow;
        WeaponBalanceWindow weaponBalanceWindow;
        ArmourBalanceWindow armourBalanceWindow;

        void OnBarterSimulator(object sender, RoutedEventArgs e) { ShowBarterSimulator(); }
        void OnWeaponBalance(object sender, RoutedEventArgs e) { ShowWeaponBalance(); }
        void OnArmourBalance(object sender, RoutedEventArgs e) { ShowArmourBalance(); }

        /// <summary>Tools ▸ Balance ▸ Barter simulator.</summary>
        public BarterSimulatorWindow ShowBarterSimulator()
        {
            if (vm.Session == null) return null;
            if (barterWindow != null) { barterWindow.Activate(); return barterWindow; }
            var w = barterWindow = new BarterSimulatorWindow(this);
            w.Closed += (s, e) => { if (barterWindow == w) barterWindow = null; };
            w.Show(this);
            return w;
        }

        /// <summary>Tools ▸ Balance ▸ Weapons.</summary>
        public WeaponBalanceWindow ShowWeaponBalance()
        {
            if (vm.Session == null) return null;
            if (weaponBalanceWindow != null) { weaponBalanceWindow.Activate(); return weaponBalanceWindow; }
            var w = weaponBalanceWindow = new WeaponBalanceWindow(this);
            w.Closed += (s, e) => { if (weaponBalanceWindow == w) weaponBalanceWindow = null; };
            w.Show(this);
            return w;
        }

        /// <summary>Tools ▸ Balance ▸ Armour and rings.</summary>
        public ArmourBalanceWindow ShowArmourBalance()
        {
            if (vm.Session == null) return null;
            if (armourBalanceWindow != null) { armourBalanceWindow.Activate(); return armourBalanceWindow; }
            var w = armourBalanceWindow = new ArmourBalanceWindow(this);
            w.Closed += (s, e) => { if (armourBalanceWindow == w) armourBalanceWindow = null; };
            w.Show(this);
            return w;
        }
    }
}
