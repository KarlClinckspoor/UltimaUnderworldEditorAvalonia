using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>Chooses a new object and fills in a form that fits its kind (AddObjectModel).</summary>
    public partial class AddObjectWindow : Window
    {
        public AddObjectModel Model { get; private set; }
        /// <summary>The object chosen, or null when cancelled.</summary>
        public ObjectSpec Result { get; private set; }
        public bool Inside { get { return InsideBox.IsChecked == true; } }

        public AddObjectWindow() { InitializeComponent(); }

        /// <param name="where">Where it will go, said in the heading.</param>
        /// <param name="inside">The container it can go into instead, or null.</param>
        public AddObjectWindow(GameSession s, Func<int, Bitmap> icon, bool forInventory, string where, string inside = null, bool insideByDefault = false)
        {
            InitializeComponent();
            Model = new AddObjectModel(s, icon, forInventory);
            DataContext = Model;
            ManualWindow.Attach(this, "Editing levels");
            Heading.Text = forInventory ? "Add to the inventory" : "Add an object to the level";
            WhereText.Text = where;
            if (inside != null)
            {
                InsideBox.Content = "Put it inside " + inside;
                InsideBox.IsVisible = true;
                InsideBox.IsChecked = insideByDefault;
            }
            Opened += (o, e) => FilterBox.Focus();
        }

        void OnAdd(object sender, RoutedEventArgs e)
        {
            if (!Model.HasItem) return;
            Result = Model.Build();
            Close(Result);
        }

        void OnCancel(object sender, RoutedEventArgs e) { Result = null; Close(null); }

        /// <summary>Shows the window as a dialog and returns the spec chosen, or null.</summary>
        public static async Task<AddObjectWindow> Ask(Window owner, GameSession s, Func<int, Bitmap> icon, bool forInventory, string where, string inside = null, bool insideByDefault = false)
        {
            var w = new AddObjectWindow(s, icon, forInventory, where, inside, insideByDefault);
            await w.ShowDialog<ObjectSpec>(owner);
            return w;
        }
    }
}
