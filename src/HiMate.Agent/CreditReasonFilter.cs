using System;
using System.Windows.Controls;
using System.Windows.Data;
using HiMate.Agent.Models;

namespace HiMate.Agent;

public partial class MainWindow
{
    private void TopupCustomerFilter_Changed(object sender, TextChangedEventArgs e)
    {
        if (ServerTopupsGrid is null) return;
        var text = (TopupCustomerFilterBox.Text ?? "").Trim();
        var view = CollectionViewSource.GetDefaultView(_serverTopups);
        view.Filter = item =>
        {
            if (item is not TopupRequest t) return false;
            if (text.Length == 0) return true;
            return t.CustomerName.Contains(text, StringComparison.OrdinalIgnoreCase)
                || t.Phone.Contains(text, StringComparison.OrdinalIgnoreCase)
                || t.UserId.ToString().Contains(text, StringComparison.OrdinalIgnoreCase)
                || t.Note.Contains(text, StringComparison.OrdinalIgnoreCase);
        };
        view.Refresh();
    }
}
