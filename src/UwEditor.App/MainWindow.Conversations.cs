using Avalonia.Interactivity;

namespace UwEditor.App
{
    public partial class MainWindow
    {
        void OnConversationSimulator(object sender, RoutedEventArgs e) { ShowConversationSimulator(); }
        void OnConversationGraph(object sender, RoutedEventArgs e) { ShowConversationGraph(); }

        /// <summary>The conversation simulator, on a slot if given (else the one the conversation editor shows). Opens the conversation editor too.</summary>
        public ConversationSimulatorWindow ShowConversationSimulator(int slot = -1)
        {
            var cw = ShowConversations(slot);
            return cw == null ? null : cw.ShowSimulator();
        }

        /// <summary>The conversation graph, on a slot if given (else the one the conversation editor shows). Opens the conversation editor too.</summary>
        public ConversationGraphWindow ShowConversationGraph(int slot = -1)
        {
            var cw = ShowConversations(slot);
            return cw == null ? null : cw.ShowGraph();
        }
    }
}
