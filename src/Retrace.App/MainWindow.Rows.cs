using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Retrace.Core;
using Retrace.Data;

namespace Retrace.App;

public partial class MainWindow
{
    public sealed class EventRow
    {
        public required RetraceEvent Item { get; init; }
        public string Time => Item.LocalTime.ToString("HH:mm:ss");
        public string Type => Item.EventType.ToString();
        public string Name => Item.FileName;
        public string Path => Item.DisplayPath;
        public string RecoveryLabel => Item.RecoveryAvailable ? (Item.Status == "Reversed" ? "Reversed" : "Ready") : "Unavailable";
        public string RecoveryColor => Item.RecoveryAvailable ? "#65DDA6" : "#FFB86B";
        public static EventRow From(RetraceEvent item) => new() { Item = item };
    }

    public sealed class SnapshotRow
    {
        public SnapshotInfo Item { get; }
        public string Name => Item.Name;
        public string DisplayDate => Item.CreatedUtc.ToLocalTime().ToString("dddd, dd MMM yyyy • HH:mm");
        public SnapshotRow(SnapshotInfo item) => Item = item;
    }
}
