using CefSharp.Wpf;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace WachbuchApp
{

    public partial class DialogBulkEdit : Window
    {

        private MainService? _ownerService;

        // ########################################################################################

        #region Window-Start

        public DialogBulkEdit()
        {
            InitializeComponent();
        }

        internal static DialogBulkEdit GetInstance(Window owner, MainService service)
        {
            DialogBulkEdit wnd = new();
            wnd.DataContext = wnd;

            // Fenster-Variablen festlegen
            wnd.Owner = owner;
            wnd._ownerService = service;

            // BulkListe erstellen
            wnd.DataContext = wnd;
            wnd.BulkEntries = new();
            foreach (var employee in service.Database.GetBulkEmployees)
            {
                var entry = new BulkEntry() { EmployeeLabel = employee.EmployeeLabelText, Quali = employee.Qualification, Station = employee.AssignedStation, EmployeeID = employee.VivendiId };
                entry.PropertyChanged += wnd.Entry_PropertyChanged;
                wnd.BulkEntries.Add(entry);
            }
            wnd.StationsItems = new List<string>((from x in service.Configuration.Books where x.IDs != null select x.StationName).ToList());


            return wnd;
        }

        private void Entry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            
            if (_ownerService == null) { return; }
            if (sender != null)
            {
                BulkEntry target = (BulkEntry)sender;
                _ownerService.Database.SetEmployeeQualification(target.EmployeeID, target.Quali);
                _ownerService.Database.SetEmployeeAssignedStation(target.EmployeeID, target.Station);
            }

        }

        #endregion
        #region Window-Events

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.Opacity = 0;
            CalculateWindow();
            this.Opacity = 1;
        }

        private void Window_Titlebar_Exited(object sender, EventArgs e)
        {
            Close();
        }

        #endregion

        // ########################################################################################

        #region Window-WindowState

        private void CalculateWindow()
        {

            // ScaleFactor ermitteln
            PresentationSource source = PresentationSource.FromVisual(this);
            double scaleFactor = source.CompositionTarget.TransformToDevice.M11;

            // Abmessungen berechnen
            double WorkHeight = SystemParameters.WorkArea.Height;
            double WorkWidth = SystemParameters.WorkArea.Width;

            double WindowHeight = Math.Floor(WorkHeight * 0.8); // 80% vom Arbeitsbereich
            double WindowWidth = Math.Floor(WorkWidth * 0.8);

            // Fenster positionieren
            Width = WindowWidth;
            Height = WindowHeight;
            Left = WorkWidth / 2 - WindowWidth / 2;
            Top = WorkHeight / 2 - WindowHeight / 2;

        }

        #endregion

        // ########################################################################################

        #region DataGrid

        public class BulkEntry : INotifyPropertyChanged
        {

            public long EmployeeID { get; set; } = -1;
            public string EmployeeLabel { get; set; } = "";

            private EmployeeQualification _quali;
            public EmployeeQualification Quali
            {
                get { return _quali; }
                set
                {
                    _quali = value;
                    RaisePropertyChanged();
                }
            }

            private string _station = "";
            public string Station
            {
                get { return _station; }
                set
                {
                    _station = value;
                    RaisePropertyChanged();
                }
            }

            // ########################################################################################

            public event PropertyChangedEventHandler? PropertyChanged;
            private void RaisePropertyChanged([CallerMemberName] string caller = "")
            {
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs(caller));
                }
            }

        }

        public ObservableCollection<BulkEntry> BulkEntries { get; set; } = new();

        public List<string> StationsItems { get; set; } = new();

        #endregion

    }

}
