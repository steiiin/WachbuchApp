using CefSharp.Wpf;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace WachbuchApp
{

    public partial class MainWindow : Window
    {

        private readonly MainService Service;

        // ########################################################################################

        #region Window-Start

        public MainWindow()
        {

            // Fenster init & ausblenden
            InitializeComponent();
            Opacity = 0;
            ShowInTaskbar = false;

            // Handler zurücksetzen
            currentHandler = new EmptyHandler(docViewer);

            // BackgroundService starten
            Service = new();
            Service.GetDataFinished += Service_GetDataFinished;

        }

        public void StartInState(bool startMinimized)
        {

            // Wenn Preload > ausblenden, sonst normal Starten
            if (startMinimized) { MinimizeToTray(); }
            else { RestoreFromTray(); }

        }

        #endregion
        #region Window-Events

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

            // ProgressDialog anzeigen
            UpdateMessageDisplay(MessageState.STARTUP);

            // Handler & Wachbuch-Switcher erstellen
            stackHandlerSelector.Children.Clear();
            foreach (var book in Service.Configuration.Books)
            {
                var handler = new BookHandler(docViewer, book);
                AddHandlerSelectorButton(handler);
            }

        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            MinimizeToTray();
            e.Cancel = true;
        }

        private void Window_CalendarCaptured(object sender, MouseEventArgs e)
        {
            if (e.MouseDevice.Captured is Calendar || e.MouseDevice.Captured is System.Windows.Controls.Primitives.CalendarItem)
            {
                e.MouseDevice.Capture(null);
            }
        }

        private void Window_Titlebar_Exited(object sender, EventArgs e)
        {
            Close();
        }

        #endregion

        // ########################################################################################

        #region Window-WindowState

        public void MinimizeToTray()
        {

            // Evtl. abmelden
            PrivateModeLogout();

            // Private Daten löschen
            Service.Database.ClearPrivateCache();

            // Fenster ausblenden
            Opacity = 0;
            ShowInTaskbar = false;
            Hide();

        }

        public void RestoreFromTray()
        {

            // Window anzeigen
            Opacity = 0;
            ShowInTaskbar = true;
            Show();
            CalculateWindow();
            Opacity = 1;

            // Windows in Vordergrund
            Topmost = true;
            Activate();
            Topmost = false;

            // UI zurücksetzen
            PrivateModeLogout();
            SwitchToPublicMode();
            ResetStatusbar();

            // Datum zurücksetzen, wenn Browser initialisiert ist
            if (docViewer.IsInitialized)
            {
                Button firstBtn = (Button)stackHandlerSelector.Children[0];
                if (!firstBtn.IsDefault) firstBtn.RaiseEvent(new(ButtonBase.ClickEvent));
                DocViewer_IsBrowserInitializedChanged(docViewer, new());
            }

        }

        private void CalculateWindow()
        {

            // ScaleFactor ermitteln
            PresentationSource source = PresentationSource.FromVisual(this);
            double scaleFactor = source.CompositionTarget.TransformToDevice.M11;

            // Abmessungen berechnen
            double WorkHeight = SystemParameters.WorkArea.Height;
            double WorkWidth = SystemParameters.WorkArea.Width;

            double WindowHeight = Math.Floor(WorkHeight * 0.9); // 90% vom Arbeitsbereich
            double browserWidth = Math.Floor(21.5 * (scaleFactor * 96) / 2.54d) + SystemParameters.VerticalScrollBarWidth;
            double WindowWidth = browserWidth + 180 /* VerticalTab */ + 305 /* Kalender */;

            if (WorkWidth < WindowWidth)
            {
                MessageBox.Show(MainServiceHelper.GetString("MainWindow_Error_ScreenComp"));
                Application.Current.Shutdown(-1);
                return;
            }

            // Fenster positionieren
            Width = WindowWidth;
            Height = WindowHeight;
            Left = WorkWidth / 2 - WindowWidth / 2;
            Top = WorkHeight / 2 - WindowHeight / 2;

            // Browser anpassen
            columnBrowserWidth.Width = new(browserWidth);

        }

        #endregion

        #region Window-Dialog/Benachrichtigung

        private readonly Stopwatch _nagCooldown = new();

        private enum MessageState
        {
            STARTUP,
            STARTUP_LOADED,

            SWITCH_STATE,

            ERROR_CREDENTIAL,
            ERROR_TOKENEXPIRED,
            ERROR_CONNECTION,
            ERROR_SERVER,

            PRIVATE_ERROR,

            OK
        }

        private void UpdateMessageDisplay(MessageState State, DateTime? FetchedDate = null, bool IsDataAvailable = false)
        {

            switch (State)
            {

                // Beim Laden des Fensters
                case MessageState.STARTUP:

                    overlayDialog.Visibility = Visibility.Visible;
                    RefreshActions(false);
                    return;

                // Wenn Browser geladen
                case MessageState.STARTUP_LOADED:

                    overlayDialog.Visibility = Visibility.Collapsed;
                    return;

                // Successful
                case MessageState.OK:

                    HideProgress();
                    HideError();

                    SetStatusbar(StatusText: MainServiceHelper.GetString("Status_FreshData"));
                    RefreshActions(true);
                    return;

                // Wenn Datum oder Handler gewechselt wird
                case MessageState.SWITCH_STATE:

                    ShowProgress(ProgressText: MainServiceHelper.GetString("Status_Load"));
                    return;

                // /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////// // Fehlermeldungen

                // CredentialError > Zeige ein Loginfenster an
                case MessageState.ERROR_CREDENTIAL:

                    HideProgress();
                    SetStatusbar(StatusText: MainServiceHelper.GetString("Status_Login"), ShowProgressIndicator: true);

                    // Dialog anzeigen, wenn keine Daten vorhanden oder Letzte Loginaufforderung vor mehr als 5 Minuten
                    if (!IsDataAvailable || !_nagCooldown.IsRunning || _nagCooldown.Elapsed.TotalMinutes > 5)
                    {

                        DialogLogin dialogLogin = DialogLogin.GetPublicInstance(this, Service);
                        if (MainServiceHelper.ShowDialog(overlayDialog, dialogLogin) == true)
                        {

                            // Erfolgreich angemeldet
                            UpdateDate(ForceUpdate: true);

                            // Nag zurücksetzen
                            _nagCooldown.Reset();

                            // Abbruch
                            return;

                        }

                    }

                    // Cooldown starten
                    if (!_nagCooldown.IsRunning) { _nagCooldown.Restart(); }

                    // Keine Anmeldung vorhanden
                    var btnRelogin = new KeyValuePair<string, Action>(MainServiceHelper.GetString("Common_Button_Login"), () => { _nagCooldown.Reset(); UpdateDate(ForceUpdate: true); return; });
                    if (IsDataAvailable)
                    {
                        // Wenn Daten vorhanden > FlyOut
                        ShowError(IsFullscreenMessage: false, Title: MainServiceHelper.GetString("MainWindow_ErrorCredentials_Title"), Message: MainServiceHelper.GetString("MainWindow_ErrorCredentials_FlyoutMessage"), Icon: MessageBoxImage.Warning, ActionButton: btnRelogin);
                        SetStatusbar(StatusText: string.Format(MainServiceHelper.GetString("Status_OldData"), MainServiceHelper.ConvertDateHumanReadable(FetchedDate ?? DateTime.Now)));
                    }
                    else
                    {
                        // Wenn Keine > DocViewer sperren & Meldung
                        ShowError(IsFullscreenMessage: true, Title: MainServiceHelper.GetString("MainWindow_ErrorCredentials_Title"), Message: MainServiceHelper.GetString("MainWindow_ErrorCredentials_FullMessage"), MessageBoxImage.Error, ActionButton: btnRelogin);
                        SetStatusbar(StatusText: MainServiceHelper.GetString("Status_NoData"));
                    }

                    break;

                // TokenExpired > Zeige Meldung an und wechsele dann auf CredentialError
                case MessageState.ERROR_TOKENEXPIRED:

                    DialogMessageBox msgExpired = DialogMessageBox.GetInstance(this, MainServiceHelper.GetString("MainWindow_ErrorExpired_Title"), MainServiceHelper.GetString("MainWindow_ErrorExpired_Message"), MessageBoxImage.Error, new(MainServiceHelper.GetString("Common_Button_Ok"),
                    () => { UpdateDate(ForceUpdate: true); return; }));
                    MainServiceHelper.ShowDialog(overlayDialog, msgExpired);
                    break;

                // ConnectionError 
                case MessageState.ERROR_CONNECTION:

                    var btnConnectionRetry = new KeyValuePair<string, Action>(MainServiceHelper.GetString("Common_Button_Retry"), () => { UpdateDate(ForceUpdate: true); return; });
                    if (IsDataAvailable)
                    {
                        // Wenn Daten vorhanden > FlyOut
                        ShowError(IsFullscreenMessage: false, Title: MainServiceHelper.GetString("MainWindow_ErrorConnection_Title"), Message: MainServiceHelper.GetString("MainWindow_ErrorConnection_FlyoutMessage"), Icon: MessageBoxImage.Warning, ActionButton: btnConnectionRetry);
                        SetStatusbar(StatusText: string.Format(MainServiceHelper.GetString("Status_OldData"), MainServiceHelper.ConvertDateHumanReadable(FetchedDate ?? DateTime.Now)));
                    }
                    else
                    {
                        // Wenn Keine > DocViewer sperren & Meldung
                        ShowError(IsFullscreenMessage: true, Title: MainServiceHelper.GetString("MainWindow_ErrorConnection_Title"), Message: MainServiceHelper.GetString("MainWindow_ErrorConnection_FullMessage"), MessageBoxImage.Error, ActionButton: btnConnectionRetry);
                        SetStatusbar(StatusText: MainServiceHelper.GetString("Status_NoData"));
                    }
                    break;

                // ServerError 
                case MessageState.ERROR_SERVER:

                    var btnErrorRetry = new KeyValuePair<string, Action>(MainServiceHelper.GetString("Common_Button_Retry"), () => { UpdateDate(ForceUpdate: true); return; });
                    if (IsDataAvailable)
                    {
                        // Wenn Daten vorhanden > FlyOut
                        ShowError(IsFullscreenMessage: false, Title: MainServiceHelper.GetString("MainWindow_ErrorServer_Title"), Message: MainServiceHelper.GetString("MainWindow_ErrorServer_FlyoutMessage"), Icon: MessageBoxImage.Warning, ActionButton: btnErrorRetry);
                        SetStatusbar(StatusText: string.Format(MainServiceHelper.GetString("Status_OldData"), MainServiceHelper.ConvertDateHumanReadable(FetchedDate ?? DateTime.Now)));
                    }
                    else
                    {
                        // Wenn Keine > DocViewer sperren & Meldung
                        ShowError(IsFullscreenMessage: true, Title: MainServiceHelper.GetString("MainWindow_ErrorServer_Title"), Message: MainServiceHelper.GetString("MainWindow_ErrorServer_FullMessage"), MessageBoxImage.Error, ActionButton: btnErrorRetry);
                        SetStatusbar(StatusText: MainServiceHelper.GetString("Status_NoData"));
                    }
                    break;

                // /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////// // Fehlermeldungen

                // PrivateError
                case MessageState.PRIVATE_ERROR:

                    HideProgress();

                    DialogMessageBox msgPrivateError = DialogMessageBox.GetInstance(this, MainServiceHelper.GetString("MainWindow_PlanHandler_Title"), MainServiceHelper.GetString("MainWindow_Private_Error"), MessageBoxImage.Error, new(MainServiceHelper.GetString("Common_Button_Ok"), () => { }));
                    MainServiceHelper.ShowDialog(overlayDialog, msgPrivateError);
                    break;

            }

            RefreshActions(IsDataAvailable);

        }

        // ########################################################################################

        private void ShowProgress(string ProgressText = "")
        {

            HideError();
            overlayProgress.Visibility = Visibility.Visible;

            calendarInput.IsEnabled = false;
            monthInput.IsEnabled = false;
            stackActionButtons.IsEnabled = false;

            SetStatusbar(string.IsNullOrWhiteSpace(ProgressText) ? MainServiceHelper.GetString("Status_Load") : ProgressText, true);
        }

        private void HideProgress()
        {
            overlayProgress.Visibility = Visibility.Collapsed;

            calendarInput.IsEnabled = true;
            monthInput.IsEnabled = true;
            stackActionButtons.IsEnabled = true;

            ResetStatusbar();
        }

        // ########################################################################################

        private void SetStatusbar(string StatusText = "", bool ShowProgressIndicator = false)
        {

            // ProgressBar anzeigen
            statusProgressbar.Visibility = (ShowProgressIndicator ? Visibility.Visible : Visibility.Collapsed);

            // StatusText anzeigen
            statusProgressText.Text = StatusText;
            statusProgressText.Visibility = (string.IsNullOrWhiteSpace(StatusText) ? Visibility.Collapsed : Visibility.Visible);

        }

        private void ResetStatusbar()
        {
            SetStatusbar(StatusText: MainServiceHelper.GetString("Status_OK"), ShowProgressIndicator: false);
        }

        // ########################################################################################

        private void ShowError(bool IsFullscreenMessage = false, string Title = "", string Message = "", MessageBoxImage Icon = MessageBoxImage.None, KeyValuePair<string, Action>? ActionButton = null)
        {

            HideProgress();

            if (IsFullscreenMessage)
            {

                // Zeige Fullscreen-Meldung
                overlayErrorFlyout.Visibility = Visibility.Collapsed;
                overlayErrorFullscreenMessageField.SetMessageField(Title, Message, Icon, ActionButton);
                overlayErrorFullscreen.Visibility = Visibility.Visible;
                overlayProgress.Visibility = Visibility.Visible;

            }
            else
            {

                // Zeige nur Hinweis unten
                overlayErrorFullscreen.Visibility = Visibility.Collapsed;
                overlayErrorFlyoutMessageField.SetMessageField(Title, Message, Icon, ActionButton);
                overlayErrorFlyout.Visibility = Visibility.Visible;

            }

        }

        private void HideError()
        {
            overlayErrorFlyout.Visibility = Visibility.Collapsed;
            overlayErrorFullscreen.Visibility = Visibility.Collapsed;
        }

        #endregion

        // ########################################################################################

        #region Service-Events

        private void Service_GetDataFinished(DateTime fetchDate, MainServiceFetchDataState state)
        {

            switch (state.FetchState)
            {

                // Bei einem Anmeldefehler > Login anzeigen
                case VivendiApiState.CREDENTIALS_ERROR:

                    UpdateMessageDisplay(MessageState.ERROR_CREDENTIAL, FetchedDate: state.AvailabeDataLastFetched, IsDataAvailable: state.IsDataAvailable);
                    break;

                // Wenn Token abgelaufen > Fehlermeldung (Kann nicht hier sondern nur auf der Webseite behoben werden)
                case VivendiApiState.OK_BUT_EXPIRED:

                    UpdateMessageDisplay(MessageState.ERROR_TOKENEXPIRED, FetchedDate: state.AvailabeDataLastFetched, IsDataAvailable: state.IsDataAvailable);
                    break;

                // Keine Verbindung
                case VivendiApiState.CONNECTION_ERROR:

                    UpdateMessageDisplay(MessageState.ERROR_CONNECTION, FetchedDate: state.AvailabeDataLastFetched, IsDataAvailable: state.IsDataAvailable);
                    break;

                // Keine Antwort vom Server / AppFehler
                case VivendiApiState.SERVER_APP_ERROR:

                    UpdateMessageDisplay(MessageState.ERROR_SERVER, FetchedDate: state.AvailabeDataLastFetched, IsDataAvailable: state.IsDataAvailable);
                    break;

                case VivendiApiState.SUCCESSFUL:

                    UpdateMessageDisplay(MessageState.OK);
                    break;

            }

        }

        #endregion

        #region DocViewer-Events

        private void DocViewer_IsBrowserInitializedChanged(object sender, DependencyPropertyChangedEventArgs e)
        {

            // Oberfläche aktivieren
            UpdateMessageDisplay(MessageState.STARTUP_LOADED);

            // Kalender auf aktuellen Tag einstellen beim ersten Start
            if (calendarInput.SelectedDate == null || calendarInput.SelectedDate != DateTime.Now)
            {
                calendarInput.SelectedDate = DateTime.Now;
                monthInput.SelectedDate = DateTime.Now;
            }

        }

        private void DocViewer_LoadingStateChanged(object sender, CefSharp.LoadingStateChangedEventArgs e)
        {

            // Wenn Seite geladen > Im UI-Thread ausführen
            if (!e.IsLoading)
            {

                if (currentHandler != null)
                {

                    // docViewer läuft in eigenem Thread > Handler muss per Invoke gesteuert werden
                    Application.Current.Dispatcher.Invoke(async () =>
                    {



                        if (calendarInput.SelectedDate == null ||
                            monthInput.SelectedDate == null) { return; }

                        UpdateMessageDisplay(MessageState.SWITCH_STATE);
                        if (currentHandler.GetType() == typeof(PlanHandler))
                        {
                            await currentHandler.UpdateSource(monthInput.SelectedDate.Value, Service);
                        }
                        else
                        {
                            await currentHandler.UpdateSource(calendarInput.SelectedDate.Value, Service);
                        }

                    });
                }

            }

        }

        private void DocViewer_JavascriptMessageReceived(object sender, CefSharp.JavascriptMessageReceivedEventArgs e)
        {

            // docViewer läuft in eigenem Thread > Handler muss per Invoke gesteuert werden
            Application.Current.Dispatcher.Invoke(() =>
            {

                // Event für BookHandler abfangen
                if (currentHandler.GetType() == typeof(BookHandler))
                {

                    // Abbruch, wenn kein Datum gewählt
                    if (calendarInput.SelectedDate == null) { return; }
                    DateTime selectedDate = calendarInput.SelectedDate.Value;

                    DocViewerEvent @event = new(e.Message.ToString(), (BookHandler)currentHandler);
                    switch (@event.Action)
                    {

                        // Mitarbeiter ändern
                        case DocViewerEvent.EventAction.EDIT_EMPLOYEE:

                            string modText = ((BookHandler)currentHandler).GetModEmployeeText(@event.LabelId, selectedDate) ?? string.Empty;

                            DialogEditEmployee editEmployee = DialogEditEmployee.GetInstance(this, Service, (string.IsNullOrEmpty(modText) ? DialogEditEmployeeEditAction.EDIT_QUALIFICATION : DialogEditEmployeeEditAction.EDIT_ENTRYTEXT), @event.EmployeeId, modText);
                            if (MainServiceHelper.ShowDialog(overlayDialog, editEmployee) == true)
                            {

                                switch (DialogEditEmployee.SelectedAction)
                                {
                                    case DialogEditEmployeeEditAction.EDIT_QUALIFICATION:

                                        // Nix zu tun, da im Dialog gespeichert
                                        break;

                                    case DialogEditEmployeeEditAction.EDIT_ENTRYTEXT:

                                        ((BookHandler)currentHandler).ModifyEntryText(@event.LabelId, selectedDate, DialogEditEmployee.SelectedBookEntryText);
                                        break;

                                }

                                UpdateDate();

                            }
                            break;

                        // Innendienst bearbeiten
                        case DocViewerEvent.EventAction.EDIT_ID:

                            DialogEditIdEditEntry modEntry = ((BookHandler)currentHandler).GetModIdEntry(@event.LabelId, selectedDate) ??
                                new(((BookHandler)currentHandler).GetLinkedId(@event.LabelId), "");

                            DialogEditID editID = DialogEditID.GetInstance(this, Service, modEntry, (modEntry.IsEmpty ? DialogEditIdEditAction.EDIT_ASSIGNEDSTATION : DialogEditIdEditAction.EDIT_ENTRY), @event.EmployeeId);
                            if (MainServiceHelper.ShowDialog(overlayDialog, editID) == true)
                            {

                                switch (DialogEditID.SelectedAction)
                                {
                                    case DialogEditIdEditAction.EDIT_ASSIGNEDSTATION:

                                        // Nix zu tun, da im Dialog gespeichert
                                        break;

                                    case DialogEditIdEditAction.EDIT_ENTRY:

                                        ((BookHandler)currentHandler).ModifyIdEntryText(@event.LabelId, selectedDate, DialogEditID.SelectedIdEntry.TypeShort, DialogEditID.SelectedIdEntry.EmployeeText);
                                        break;

                                }

                                UpdateDate();

                            }
                            break;

                        // Fahrzeug bearbeiten
                        case DocViewerEvent.EventAction.EDIT_VEHICLE:

                            DialogEditVehicle editVehicle = DialogEditVehicle.GetInstance(this, Service, ((BookHandler)currentHandler).GetBookVehicles, @event.Vehicle);
                            if (MainServiceHelper.ShowDialog(overlayDialog, editVehicle) == true)
                            {

                                ((BookHandler)currentHandler).ModifyVehicle(@event.LabelId, DialogEditVehicle.SelectedVehicle);
                                UpdateDate();

                            }

                            break;

                    }

                }

            });

        }

        #endregion

        #region Calendar-Events

        private void CalendarInput_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
        {

            // Anzeigedatum korrigieren
            if (calendarInput.SelectedDate != null)
            {
                if (calendarInput.DisplayDate.Year != calendarInput.SelectedDate.Value.Year ||
                    calendarInput.DisplayDate.Month != calendarInput.SelectedDate.Value.Month)
                {
                    calendarInput.DisplayDate = calendarInput.SelectedDate.Value;
                }
            }

            // Aktualisieren für einzelnen Tag > BookHandler
            UpdateDate();
        }

        private void CalendarInput_KeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
        }

        private async void UpdateDate(bool ForceUpdate = false)
        {

            // Abbruch, wenn kein Datum gewählt
            if (calendarInput.SelectedDate == null) { return; }
            DateTime selectedDate = calendarInput.SelectedDate.Value;

            // ProgressDialog anzeigen
            UpdateMessageDisplay(MessageState.SWITCH_STATE);

            // Bei Datumswechsel den aktuellen Handler aktualisieren
            if (currentHandler.GetType() != typeof(BookHandler))
            {
                ((Button)stackHandlerSelector.Children[0]).IsDefault = false;
                ((Button)stackHandlerSelector.Children[0]).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            else
            {
                await currentHandler.UpdateSource(selectedDate, Service, ForceUpdate);
            }

        }

        // ########################################################################################

        private void MonthInput_DisplayDateChanged(object sender, CalendarDateChangedEventArgs e)
        {
            if (!docViewer.IsBrowserInitialized) { return; }
            monthInput.SelectedDate = monthInput.DisplayDate;
        }

        private void MonthInput_DisplayModeChanged(object sender, CalendarModeChangedEventArgs e)
        {
            if (monthInput.DisplayMode != CalendarMode.Year)
            {
                monthInput.DisplayMode = CalendarMode.Year;
            }
        }

        private void MonthInput_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
        {
            // Aktualisieren für ganzen Monat > PlanHandler
            UpdateMonth();
        }

        private async void UpdateMonth(bool ForceUpdate = false)
        {

            // Abbruch, wenn kein Datum gewählt
            if (monthInput.SelectedDate == null) { return; }
            DateTime selectedDate = monthInput.SelectedDate.Value;

            if (currentHandler.GetType() == typeof(PlanHandler))
            {
                // ProgressDialog anzeigen
                UpdateMessageDisplay(MessageState.SWITCH_STATE);

                // Bei Datumswechsel den aktuellen Handler aktualisieren
                await currentHandler.UpdateSource(selectedDate, Service, ForceUpdate);
            }

        }

        // ########################################################################################

        private Button CreateHandlerSwitchButton(DocHandler handler, bool switchPublic = false, bool switchPrivate = false)
        {

            Button b = new();
            b.Content = handler.Title;
            b.Height = 30;
            b.Margin = new Thickness(2);
            b.Click += (s, e) =>
            {

                if (((Button)s).IsDefault) { return; } // Abbrechen, wenn bereits ausgewählt

                // ButtonState ändern
                if (stackHandlerSelector.Tag != null) { ((Button)stackHandlerSelector.Tag).IsDefault = false; } // Letzten Aktiven Button abwählen
                stackHandlerSelector.Tag = b; // Aktuellen Button als Aktiv speichern
                b.IsDefault = true;

                // Handle wechseln
                UpdateMessageDisplay(MessageState.SWITCH_STATE);
                currentHandler = handler;
                currentHandler.SwitchTo();

                // Modus wechseln
                if (switchPublic) { SwitchToPublicMode(); }
                if (switchPrivate) { SwitchToPrivateMode(); }

            };
            return b;

        }

        private void AddHandlerSelectorButton(DocHandler handler)
        {
            stackHandlerSelector.Children.Add(CreateHandlerSwitchButton(handler, switchPublic: true));
        }

        #endregion

        // ########################################################################################

        #region Private/Public-Mode

        private Button? PrivateHandlerSelectButton;
        private VivendiApiMasterDataResponse? PrivateMasterData;

        public void SwitchToPrivateMode()
        {

            // Kalender 
            monthInput.Visibility = Visibility.Visible;
            calendarInput.Visibility = Visibility.Collapsed;

        }

        public void SwitchToPublicMode()
        {

            // Kalender
            monthInput.Visibility = Visibility.Collapsed;
            calendarInput.Visibility = Visibility.Visible;

        }

        #endregion

        #region DocHandler

        private DocHandler currentHandler;

        // ########################################################################################

        private abstract class DocHandler
        {

            public abstract string Title { get; }
            public abstract bool IsModified { get; }

            public abstract void SwitchTo();
            public abstract Task UpdateSource(DateTime dataDate, MainService mainService, bool ForceUpdate = false);

        }

        private class EmptyHandler : DocHandler
        {

            private readonly ChromiumWebBrowser host;

            public EmptyHandler(ChromiumWebBrowser hostBrowser) { host = hostBrowser; }

            public override string Title => "";
            public override bool IsModified => false;
            public override void SwitchTo()
            {
                host.Load(MainServiceHelper.GetDocPath("docPreload.html"));
            }
            public override Task UpdateSource(DateTime dataDate, MainService mainService, bool ForceUpdate = false)
            {
                return Task.CompletedTask;
            }

        }

        private class BookHandler : DocHandler
        {

            private readonly ChromiumWebBrowser host;
            private readonly MainServiceConfiguration.Book book;

            public BookHandler(ChromiumWebBrowser bookBrowser, MainServiceConfiguration.Book config)
            {
                host = bookBrowser;
                book = config;

                modVehicles = new();
                modEmployee = new();
                modIDs = new();

                linkEmployee = new();
                linkIDs = new();
                linkVehicles = new();
            }

            // ####################################################################################

            private readonly Dictionary<string, string> modEmployee;
            private readonly Dictionary<string, DialogEditIdEditEntry> modIDs;
            private readonly Dictionary<string, MainServiceConfiguration.BookVehicle> modVehicles;

            private Dictionary<string, long> linkEmployee;
            private Dictionary<string, string> linkIDs;
            private Dictionary<string, MainServiceConfiguration.BookVehicle> linkVehicles;

            // ####################################################################################

            public override string Title => book.StationName;

            public override bool IsModified => modVehicles.Any() || modEmployee.Any() || modIDs.Any();

            public override void SwitchTo()
            {
                host.Load(MainServiceHelper.GetDocPath(book.DocFile));
            }

            public override async Task UpdateSource(DateTime bookDate, MainService service, bool ForceUpdate = false)
            {

                if (_updateLock && !ForceUpdate) { return; }
                _updateLock = true;

                // Datum eintragen
                MainServiceHelper.SetHtmlInnerText(host, book.LabelDate, bookDate.ToString("dddd, dd. MMMM yyyy"));

                // Daten abrufen / Wenn Abruf fehlgeschlagen & keine Daten vorhanden > Anzeige löschen & Abbruch
                var state = await service.GetPublicData(bookDate);
                if (state.IsFailed && !state.IsDataAvailable)
                {
                    foreach (var bookShift in book.Shifts)
                    {
                        MainServiceHelper.ClearHtmlInnerText(host, bookShift.LabelEmp1);
                        MainServiceHelper.ClearHtmlInnerText(host, bookShift.LabelEmp2);
                        MainServiceHelper.ClearHtmlInnerText(host, bookShift.LabelEmpH);
                    }

                    linkEmployee = new();
                    linkIDs = new();
                    linkVehicles = new();

                    _updateLock = false;
                    return;
                }

                // Schichten durchlaufen
                linkEmployee.Clear();
                linkIDs.Clear();
                linkVehicles.Clear();
                foreach (var bookShift in book.Shifts)
                {

                    var shift = service.Database.GetShift(bookDate, bookShift.ConfigKey);

                    // Mitarbeiter suchen & zuweisen
                    var EmployeeList = service.Database.GetBoundEmployee(shift);
                    if (EmployeeList.Count == 0)
                    {
                        ClearEmployeeEntry(bookShift.LabelEmp1, bookDate);
                        ClearEmployeeEntry(bookShift.LabelEmp2, bookDate);
                    }
                    else if (EmployeeList.Count == 1)
                    {
                        SetEmployeeEntry(bookShift.LabelEmp1!, bookDate, EmployeeList[0].EmployeeLabelText);
                        ClearEmployeeEntry(bookShift.LabelEmp2, bookDate);

                        linkEmployee.Add(bookShift.LabelEmp1!, EmployeeList[0].VivendiId);
                    }
                    else if (EmployeeList.Count == 2)
                    {
                        SetEmployeeEntry(bookShift.LabelEmp1, bookDate, EmployeeList[0].EmployeeLabelText);
                        SetEmployeeEntry(bookShift.LabelEmp2, bookDate, EmployeeList[1].EmployeeLabelText);

                        linkEmployee.Add(bookShift.LabelEmp1!, EmployeeList[0].VivendiId);
                        linkEmployee.Add(bookShift.LabelEmp2!, EmployeeList[1].VivendiId);
                    }
                    else
                    {
                        SetEmployeeEntry(bookShift.LabelEmp1, bookDate, EmployeeList[0].EmployeeLabelText);
                        SetEmployeeEntry(bookShift.LabelEmp2, bookDate, EmployeeList[1].EmployeeLabelText);

                        linkEmployee.Add(bookShift.LabelEmp1!, EmployeeList[0].VivendiId);
                        linkEmployee.Add(bookShift.LabelEmp2!, EmployeeList[1].VivendiId);

                        if (EmployeeList.Count > 3)
                        {
                            AppLog.Error(MainServiceHelper.GetString("MainWindow_BookHandler_WarnExceed"), bookDate.ToShortDateString() + "##" + bookShift.ConfigKey);
                        }
                    }

                    // Auf Praktikanten prüfen & zuweisen
                    var traineeShift = bookShift.TraineeKey != null ? service.Database.GetShift(bookDate, bookShift.TraineeKey) : null;
                    var traineeEmp = traineeShift != null ? service.Database.GetBoundEmployee(traineeShift).FirstOrDefault() : null;
                    if (traineeEmp == null)
                    {
                        ClearEmployeeEntry(bookShift.LabelEmpH, bookDate);
                    }
                    else
                    {
                        SetEmployeeEntry(bookShift.LabelEmpH, bookDate, traineeEmp.EmployeeLabelText);
                        linkEmployee.Add(bookShift.LabelEmpH!, traineeEmp.VivendiId);
                    }

                    // Wenn keine Schicht gefunden
                    if (shift == null)
                    {

                        MainServiceHelper.ClearHtmlInnerText(host, bookShift.LabelFunk);
                        MainServiceHelper.ClearHtmlInnerText(host, bookShift.LabelKeyplate);
                        MainServiceHelper.ClearHtmlInnerText(host, bookShift.LabelTimes);

                        MainServiceHelper.SetHtmlClassNoData(host, bookShift.LabelEmpty);
                        continue;
                    }
                    else
                    {
                        MainServiceHelper.RemoveHtmlClassNoData(host, bookShift.LabelEmpty);
                    }

                    // Fahrzeugdaten zuweisen
                    if (bookShift.DefaultVehicle != null && bookShift.LabelFunk != null)
                    {

                        MainServiceConfiguration.BookVehicle vehicle = new("", "");
                        if (modVehicles.ContainsKey(bookShift.LabelVehicleKey))
                        {
                            vehicle = modVehicles[bookShift.LabelVehicleKey];
                        }
                        else
                        {
                            vehicle = (from x in book.Vehicles where x.FunkId == bookShift.DefaultVehicle select x).FirstOrDefault(vehicle);
                        }

                        MainServiceHelper.SetHtmlInnerText(host, bookShift.LabelFunk, vehicle.FunkId);
                        MainServiceHelper.SetHtmlInnerText(host, bookShift.LabelKeyplate, vehicle.Keyplate);

                        linkVehicles.Add(bookShift.LabelFunk, vehicle);

                    }

                    // Dienstzeiten zuweisen
                    if (bookShift.LabelTimes != null)
                    {
                        MainServiceHelper.SetHtmlInnerText(host, bookShift.LabelTimes, string.Format("{0} - {1}", shift.TimeStart.ToString("HH:mm"), shift.TimeEnd.ToString("HH:mm")));
                    }

                }

                // Innendienste eintragen
                if (book.IDs != null)
                {

                    // Aus Vivendi übernehmen
                    List<KeyValuePair<MainServiceDatabase.Shift, MainServiceDatabase.Employee>> idList = new();
                    foreach (var bookIdShift in service.Configuration.BookDefaults.IdKeys)
                    {

                        var shift = service.Database.GetShift(bookDate, bookIdShift);
                        if (shift == null) { continue; }

                        var EmployeeList = service.Database.GetBoundEmployee(shift);
                        foreach (var emp in EmployeeList)
                        {

                            if (service.Configuration.BookDefaults.IdIgnoredEmployees.Contains(emp.VivendiId)) { continue; }
                            if (emp.AssignedStation != book.StationName) { continue; }

                            idList.Add(new(shift, emp));

                        }

                    }

                    // In Reihenfolge übernehmen
                    for (int i = 1; i <= book.IDs.MaxPlaces; i++)
                    {
                        KeyValuePair<MainServiceDatabase.Shift, MainServiceDatabase.Employee>? entry = idList.Any() ? idList.First() : null;
                        if (entry == null)
                        {
                            ClearIdEntry(i, bookDate);
                            continue;
                        }

                        SetIdEntry(i, bookDate, entry.Value.Key.ShortName, entry.Value.Value.EmployeeLabelText);
                        linkIDs.Add(string.Format("id-emp{0}", i), entry.Value.Key.ShortName);
                        linkEmployee.Add(string.Format("id-emp{0}", i), entry.Value.Value.VivendiId);

                        if (idList.Any()) { idList.RemoveAt(0); }
                    }

                }

                // Schriftgrößen einpassen
                MainServiceHelper.SetHtmlMatchFontSizes(host);

                _updateLock = false;

            }

            // ####################################################################################

            private void SetEmployeeEntry(string? LabelId, DateTime LabelDate, string Text)
            {
                if (LabelId == null) { return; }

                if (modEmployee.ContainsKey(GetModifyEmployeeKey(LabelId, LabelDate)))
                {
                    MainServiceHelper.SetHtmlInnerText(host, LabelId, GetModEmployeeText(LabelId, LabelDate));
                }
                else
                {
                    MainServiceHelper.SetHtmlInnerText(host, LabelId, Text);
                }
            }

            private void ClearEmployeeEntry(string? LabelId, DateTime LabelDate)
            {
                if (LabelId == null) { return; }

                if (modEmployee.ContainsKey(GetModifyEmployeeKey(LabelId, LabelDate)))
                {
                    MainServiceHelper.SetHtmlInnerText(host, LabelId, GetModEmployeeText(LabelId, LabelDate));
                }
                else
                {
                    MainServiceHelper.ClearHtmlInnerText(host, LabelId);
                }
            }

            // ####################################################################################

            private void SetIdEntry(int index, DateTime LabelDate, string type, string name)
            {
                string LabelId = string.Format("id-emp{0}", index);
                if (modIDs.ContainsKey(GetModifyEmployeeKey(LabelId, LabelDate)))
                {
                    var mod = GetModIdEntry(LabelId, LabelDate);
                    MainServiceHelper.SetHtmlInnerText(host, string.Format("idtyp-emp{0}", index), mod!.TypeShort);
                    MainServiceHelper.SetHtmlInnerText(host, string.Format("idemp-emp{0}", index), mod!.EmployeeText);
                }
                else
                {
                    MainServiceHelper.SetHtmlInnerText(host, string.Format("idtyp-emp{0}", index), type);
                    MainServiceHelper.SetHtmlInnerText(host, string.Format("idemp-emp{0}", index), name);
                }
            }

            private void ClearIdEntry(int index, DateTime LabelDate)
            {
                string LabelId = string.Format("id-emp{0}", index);
                if (modIDs.ContainsKey(GetModifyEmployeeKey(LabelId, LabelDate)))
                {
                    var mod = GetModIdEntry(LabelId, LabelDate);
                    MainServiceHelper.SetHtmlInnerText(host, string.Format("idtyp-emp{0}", index), mod!.TypeShort);
                    MainServiceHelper.SetHtmlInnerText(host, string.Format("idemp-emp{0}", index), mod!.EmployeeText);
                }
                else
                {
                    MainServiceHelper.ClearHtmlInnerText(host, string.Format("idtyp-emp{0}", index));
                    MainServiceHelper.ClearHtmlInnerText(host, string.Format("idemp-emp{0}", index));
                }
            }

            // ####################################################################################

            public long GetLinkedEmployee(string key)
            {
                if (linkEmployee.ContainsKey(key))
                {
                    return linkEmployee[key];
                }
                return -1;
            }

            public string? GetModEmployeeText(string LabelId, DateTime LabelDate)
            {
                if (modEmployee.ContainsKey(GetModifyEmployeeKey(LabelId, LabelDate)))
                {
                    return modEmployee[GetModifyEmployeeKey(LabelId, LabelDate)];
                }
                return null;
            }

            // ####################################################################################

            public string GetLinkedId(string key)
            {
                if (linkIDs.ContainsKey(key))
                {
                    return linkIDs[key];
                }
                return "";
            }

            public DialogEditIdEditEntry? GetModIdEntry(string LabelId, DateTime LabelDate)
            {
                if (modIDs.ContainsKey(GetModifyEmployeeKey(LabelId, LabelDate)))
                {
                    return modIDs[GetModifyEmployeeKey(LabelId, LabelDate)];
                }
                return null;
            }

            // ####################################################################################

            public List<MainServiceConfiguration.BookVehicle> GetBookVehicles => book.Vehicles;

            public MainServiceConfiguration.BookVehicle GetLinkedVehicle(string key)
            {

                foreach (var vehicle in linkVehicles ?? new())
                {
                    if (vehicle.Key.StartsWith(key)) { return vehicle.Value; }
                }
                return new("", "");

            }

            // ####################################################################################

            public void ModifyEntryText(string Label, DateTime LabelDate, string newEntryText)
            {

                // Wenn Eintrag leer, dann Mod löschen
                if (string.IsNullOrEmpty(newEntryText)) { modEmployee.Remove(GetModifyEmployeeKey(Label, LabelDate)); return; }

                // Mod sonst aktualisieren oder hinzufügen
                if (modEmployee.ContainsKey(GetModifyEmployeeKey(Label, LabelDate))) { modEmployee[GetModifyEmployeeKey(Label, LabelDate)] = newEntryText; }
                else { modEmployee.Add(GetModifyEmployeeKey(Label, LabelDate), newEntryText); }

            }

            public void ModifyIdEntryText(string Label, DateTime LabelDate, string newIdType, string newIdText)
            {

                // Wenn Eintrag leer, dann Mod löschen
                if (string.IsNullOrEmpty(newIdText)) { modIDs.Remove(GetModifyEmployeeKey(Label, LabelDate)); return; }

                // Mod sonst aktualisieren oder hinzufügen
                if (modIDs.ContainsKey(GetModifyEmployeeKey(Label, LabelDate))) { modIDs[GetModifyEmployeeKey(Label, LabelDate)] = new(newIdType, newIdText); }
                else { modIDs.Add(GetModifyEmployeeKey(Label, LabelDate), new(newIdType, newIdText)); }

            }

            public void ModifyVehicle(string LabelKey, MainServiceConfiguration.BookVehicle newVehicle)
            {

                // Schicht zu Label finden
                var bookShift = (from x in book.Shifts where x.LabelKeyplate != null && x.LabelKeyplate.StartsWith(LabelKey) select x).First();
                if (bookShift == null) { return; }

                // Mod entfernen, wenn Neu und Default gleich sind
                if (newVehicle.Equals(bookShift.DefaultVehicle)) { modVehicles.Remove(LabelKey); return; }

                // Mod sonst aktualisieren oder hinzufügen
                if (modVehicles.ContainsKey(LabelKey)) { modVehicles[LabelKey] = newVehicle; }
                else { modVehicles.Add(LabelKey, newVehicle); }

            }

            // ####################################################################################

            private static string GetModifyEmployeeKey(string label, DateTime labelDate) => string.Format("{0}#{1}", label, labelDate.ToShortDateString());

            // ####################################################################################

            private bool _updateLock = false;

        }

        private class PlanHandler : DocHandler
        {

            private readonly ChromiumWebBrowser host;
            private readonly long privateId;

            public PlanHandler(ChromiumWebBrowser planBrowser, VivendiApiMasterDataResponse masterData)
            {
                host = planBrowser;
                privateId = masterData.EmployeeVivendiId;
            }

            // ####################################################################################

            private readonly string LabelDate = "doc-date";
            private readonly string LabelUpdated = "doc-created";

            private readonly string LabelItemShort = "dshort_";
            private readonly string LabelItemLong = "dlong_";
            private readonly string LabelItemTeam = "team_";
            private readonly string LabelItemTime = "dtimes_";

            // ####################################################################################

            public override string Title => MainServiceHelper.GetString("MainWindow_PlanHandler_Title");

            public override bool IsModified => false;

            public override void SwitchTo()
            {
                host.Load(MainServiceHelper.GetDocPath("docPrivateMonth.html"));
            }

            public override async Task UpdateSource(DateTime monthDate, MainService service, bool ForceUpdate = false)
            {

                if (_updateLock && !ForceUpdate) { return; }
                _updateLock = true;

                // Datum eintragen
                SetupMonth(monthDate);

                // Daten abrufen / Wenn Abruf fehlgeschlagen & keine Daten vorhanden > Anzeige löschen & Abbruch
                var state = await service.GetPrivateData(monthDate, privateId);
                if (state.IsFailed && !state.IsDataAvailable)
                {
                    _updateLock = false;
                    return;
                }

                // Eigene Dienste durchlaufen
                List<int> daysWritten = new();
                foreach (var shift in service.Database.GetPrivateShifts(monthDate))
                {

                    // Dienst eintragen
                    string LabelDay = shift.ShiftDate.ToString("dd");
                    MainServiceHelper.SetHtmlInnerText(host, LabelItemShort + LabelDay, shift.ShortName);
                    MainServiceHelper.SetHtmlInnerText(host, LabelItemLong + LabelDay, shift.FullName);
                    MainServiceHelper.SetHtmlInnerText(host, LabelItemTime + LabelDay, string.Format("{0} - {1}", shift.TimeStart.ToString("HH:mm"), shift.TimeEnd.ToString("HH:mm")));

                    // Evtl. TeamBuddy über öffentliche Schichtplan ziehen
                    var buddies = service.Database.GetBuddyEmployee(service.Database.GetShift(shift.ShiftDate, shift.ConfigKey), privateId);
                    string buddyString = buddies.Any() ? buddies.First().EmployeeLabelText : "";
                    if (buddies.Count > 1) { buddyString += "+ " + (buddies.Count - 1).ToString(); }
                    if (new List<string>() { "id", "rb-t", "rb-n" }.Contains(shift.ShortName.ToLower())) { buddyString = ""; }
                    MainServiceHelper.SetHtmlInnerText(host, LabelItemTeam + LabelDay, buddyString);

                    // Tag markieren
                    if (!daysWritten.Contains(shift.ShiftDate.Day)) { daysWritten.Add(shift.ShiftDate.Day); }

                }

                // Leere Tage im Plan überschreiben
                for (int day = 1; day <= 31; day++)
                {
                    if (daysWritten.Contains(day)) { continue; }
                    string LabelDay = day.ToString("00");
                    MainServiceHelper.ClearHtmlInnerText(host, LabelItemShort + LabelDay);
                    MainServiceHelper.ClearHtmlInnerText(host, LabelItemLong + LabelDay);
                    MainServiceHelper.ClearHtmlInnerText(host, LabelItemTime + LabelDay);
                    MainServiceHelper.ClearHtmlInnerText(host, LabelItemTeam + LabelDay);
                }

                _updateLock = false;

            }

            // ####################################################################################

            private void SetupMonth(DateTime monthDate)
            {

                int monthDays = DateTime.DaysInMonth(monthDate.Year, monthDate.Month);
                string monthNum = monthDate.ToString("MM");

                // Tage eintragen
                CefSharp.WebBrowserExtensions.ExecuteScriptAsyncWhenPageLoaded(host,
                    "document.body.id='d" + monthDays.ToString("00") + "';" +
                    "for (let day=1; day<=31; day++) {" +
                    "  let dayString=String(day).padStart(2, '0');" +
                    "  document.getElementById('date_' + dayString).innerText=dayString + '." + monthNum + ".'; }");

                // Titelzeile
                MainServiceHelper.SetHtmlInnerText(host, LabelDate, monthDate.ToString("MMMM yyyy"));
                MainServiceHelper.SetHtmlInnerText(host, LabelUpdated, DateTime.Now.ToString("dd.MM.yyyy"));

            }

            // ####################################################################################

            private bool _updateLock = false;

        }

        // ########################################################################################

        private class DocViewerEvent
        {

            internal DocViewerEvent(string? message, BookHandler handler)
            {

                // Wenn falscher Typ > Kein Event zurückgeben
                if (handler.GetType() != typeof(BookHandler)) { Action = EventAction.NONE; return; }

                // Payload parsen
                Action = ParseAction(message);
                string payload = ParsePayload(message);

                // Anhand Action weitere Parameter parsen
                switch (Action)
                {

                    // Mitarbeiter ändern
                    case EventAction.EDIT_EMPLOYEE:
                    case EventAction.EDIT_ID:

                        long id = handler.GetLinkedEmployee(payload);
                        LabelId = payload;
                        EmployeeId = id;
                        return;

                    // Fahrzeug ändern
                    case EventAction.EDIT_VEHICLE:

                        var vehicle = handler.GetLinkedVehicle(payload);
                        LabelId = payload;
                        Vehicle = vehicle;
                        return;

                }

            }

            // ############################################################################################

            public EventAction Action { get; private set; } = EventAction.NONE;

            public long EmployeeId { get; private set; } = -1;

            public string LabelId { get; private set; } = "";

            public MainServiceConfiguration.BookVehicle Vehicle { get; private set; } = new("", "");

            // ############################################################################################

            private static EventAction ParseAction(string? message)
            {
                if (message == null) { return EventAction.NONE; }
                if (message.Contains("EMPLOYEE")) return EventAction.EDIT_EMPLOYEE;
                if (message.Contains("VEHICLE")) return EventAction.EDIT_VEHICLE;
                if (message.Contains("ID")) return EventAction.EDIT_ID;
                return EventAction.NONE;
            }

            private static string ParsePayload(string? message)
            {
                if (message == null) { return ""; }
                var split = message.Split("|");
                if (split.Length == 2) { return split[1]; }
                return "";
            }

            // ############################################################################################

            internal enum EventAction
            {
                NONE,
                EDIT_EMPLOYEE,
                EDIT_VEHICLE,
                EDIT_ID
            }

        }

        #endregion

        #region ActionButtons

        private void RefreshActions(bool handlerReady)
        {
            if (currentHandler == null || currentHandler.GetType() == typeof(EmptyHandler))
            {

                stackActionButtons.IsEnabled = false;
                stackActionButtons.Visibility = Visibility.Hidden;

            }
            else if (currentHandler.GetType() == typeof(BookHandler))
            {

                btnActionPrintPrivate.Visibility = Visibility.Collapsed;
                btnActionSaveIcal.Visibility = Visibility.Collapsed;
                btnActionSendPrivate.Visibility = Visibility.Collapsed;
                btnActionSavePrivate.Visibility = Visibility.Collapsed;

                btnActionPrint.Visibility = Visibility.Visible;
                btnActionPrintClose.Visibility = Visibility.Visible;
                btnLogoutPrivate.IsEnabled = true;
                btnExportPrivate.IsEnabled = true;

                btnActionPrint.IsEnabled = handlerReady;
                btnActionPrintClose.IsEnabled = handlerReady;

                stackActionButtons.Visibility = Visibility.Visible;

            }
            else if (currentHandler.GetType() == typeof(PlanHandler))
            {

                btnActionPrintPrivate.Visibility = Visibility.Visible;
                btnActionSaveIcal.Visibility = Visibility.Visible;
                btnActionSendPrivate.Visibility = Visibility.Visible;
                btnActionSavePrivate.Visibility = Visibility.Visible;

                btnActionPrint.Visibility = Visibility.Collapsed;
                btnActionPrintClose.Visibility = Visibility.Collapsed;
                btnExportPrivate.IsEnabled = true;
                btnLogoutPrivate.IsEnabled = true;

                btnActionPrintPrivate.IsEnabled = handlerReady;
                btnActionSaveIcal.IsEnabled = handlerReady;
                btnActionSendPrivate.IsEnabled = handlerReady;
                btnActionSavePrivate.IsEnabled = handlerReady;

                stackActionButtons.Visibility = Visibility.Visible;

            }
        }

        // ########################################################################################

        private async void PrivateModeLogin()
        {

            // Anmeldung starten
            DialogLogin login = DialogLogin.GetPrivateInstance(this, Service);
            if (MainServiceHelper.ShowDialog(overlayDialog, login) == true)
            {

                UpdateMessageDisplay(MessageState.SWITCH_STATE);

                // Aktuelle Stammdaten abrufen
                PrivateMasterData = await Service.GetPrivateMasterData();
                if (PrivateMasterData.IsFailed)
                {
                    UpdateMessageDisplay(MessageState.PRIVATE_ERROR);
                    return;
                }

                // Handler aktualisieren
                if (PrivateHandlerSelectButton != null)
                {
                    stackHandlerSelector.Children.Remove(PrivateHandlerSelectButton);
                }
                PrivateHandlerSelectButton = CreateHandlerSwitchButton(new PlanHandler(docViewer, PrivateMasterData), switchPrivate: true);
                stackHandlerSelector.Children.Add(PrivateHandlerSelectButton);

                // Modus wechseln
                PrivateHandlerSelectButton!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                btnExportPrivate.Visibility = Visibility.Collapsed;
                btnLogoutPrivate.Visibility = Visibility.Visible;
                if (PrivateHandlerSelectButton != null) { PrivateHandlerSelectButton.Visibility = Visibility.Visible; }

                // Bildschirm aktivieren
                HideProgress();

            }

        }
        private void PrivateModeLogout()
        {

            btnExportPrivate.Visibility = Visibility.Visible;
            btnLogoutPrivate.Visibility = Visibility.Collapsed;

            if (PrivateHandlerSelectButton != null) { PrivateHandlerSelectButton.Visibility = Visibility.Collapsed; }
            if (PrivateHandlerSelectButton?.IsDefault == true)
            {
                ((Button)stackHandlerSelector.Children[0]).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }

        }

        // ########################################################################################

        private bool CreatePdf(string SAVEPATH)
        {

            try
            {

                // Dokument in Druckansicht schalten
                MainServiceHelper.SetHtmlDocMode(docViewer, PrintMode: true);

                // Altes Dokument löschen
                if (File.Exists(SAVEPATH)) { File.Delete(SAVEPATH); }

                // PDF erstellen und Speichern
                docViewer.BrowserCore.GetHost().PrintToPdf(SAVEPATH, null, null);

                // Dokument in Vorschau schalten
                MainServiceHelper.SetHtmlDocMode(docViewer, PreviewMode: true);

                return true;

            }
            catch (Exception ex)
            {
                AppLog.Error(ex);
            }
            return false;

        }

        private static bool PrintPDF(string PDFPATH)
        {

            try
            {

                // PDF ausdrucken (Silent - Per SumatraPDF) 
                // > Um keine falschen Programme auszuführen, Prüfsumme ermitteln und abgleichen
                string sumatraPath = System.IO.Path.Combine(Environment.CurrentDirectory, "printer", "sumatra.exe");

                var Sha256 = System.Security.Cryptography.SHA256.Create();
                using FileStream stream = File.OpenRead(sumatraPath);
                string sumatraHash = Convert.ToBase64String(Sha256.ComputeHash(stream));

                if (sumatraHash == "UeEYlOYZ/9imQnTGD/rcbRfG3E+MOn0zdS9fX3Yr4A8=")
                {

                    string procArguments = string.Format("-print-to-default -print-settings \"paper=A4,portrait,noscale\" -silent \"{0}\"", PDFPATH);
                    Process.Start(sumatraPath, procArguments);

                    return true;

                }

            }
            catch (Exception ex)
            {
                AppLog.Error(ex);
            }
            return false;

        }

        private string CreateIcalText(DateTime monthDate)
        {

            // Dieser Button ist erst verfügbar, wenn die Daten für den Dienstplan geladen wurden > Keine weitere Abfrage
            if (PrivateMasterData == null || PrivateMasterData.IsFailed) { return ""; }

            // Kalender erstellen
            var ical = new MainServiceHelper.ExtensionIcal();
            foreach (var privShift in Service.Database.GetPrivateShifts(monthDate))
            {

                // Evtl. TeamBuddy über öffentliche Schichtplan ziehen
                var buddies = Service.Database.GetBuddyEmployee(Service.Database.GetShift(privShift.ShiftDate, privShift.ConfigKey), PrivateMasterData.EmployeeVivendiId);
                string? buddyString = null;
                if (buddies.Any() && !new List<string>() { "id", "rb-t", "rb-n" }.Contains(privShift.ShortName.ToLower()))
                {
                    if (buddies.Count == 1)
                    {
                        buddyString = buddies.First().EmployeeLabelText;
                    }
                    else if (buddies.Count == 2)
                    {
                        buddyString = buddies.First().EmployeeNameText + " &amp; " + buddies.Last().EmployeeNameText;
                    }
                    else
                    {
                        buddyString = buddies.First().EmployeeNameText;
                        for (int i = 1; i < buddies.Count - 1; i++)
                        {
                            buddyString += ", " + buddies[i].EmployeeNameText;
                        }
                        buddyString += "& " + buddies.Last().EmployeeNameText;
                    }
                }
                ical.AddShift(privShift, buddyString);

            }

            return ical.GetIcal();

        }

        // ########################################################################################

        private async Task<bool> PrintDocViewer()
        {

            ShowProgress(ProgressText: MainServiceHelper.GetString("MainWindow_Progress_Print1"));
            await Task.Delay(500);

            bool isFailed = false;
            string tmpPath = MainServiceHelper.GetTmpPdfPath();

            // Vorschau in ein PDF speichern
            if (!CreatePdf(tmpPath)) { isFailed = true; }

            ShowProgress(ProgressText: MainServiceHelper.GetString("MainWindow_Progress_Print2"));
            await Task.Delay(500);

            // PDF drucken
            if (!PrintPDF(tmpPath)) { isFailed = true; }

            HideProgress();
            return isFailed;

        }

        private async void ButtonPrint_Click(object sender, RoutedEventArgs e)
        {

            bool isFailed = await PrintDocViewer();

            // Meldung ausgeben
            DialogMessageBox msg = DialogMessageBox.GetInstance(this,
                Title: MainServiceHelper.GetString("MainWindow_Action_PrintOnly"),
                Message: (isFailed ? MainServiceHelper.GetString("MainWindow_ErrorExport_Print_Msg") : MainServiceHelper.GetString("MainWindow_ExportSuccess_Print")),
                MessageIcon: (isFailed ? MessageBoxImage.Error : MessageBoxImage.Information),
                new(MainServiceHelper.GetString("Common_Button_Ok"), () => { }));
            MainServiceHelper.ShowDialog(overlayDialog, msg);

        }
        private async void ButtonPrintClose_Click(object sender, RoutedEventArgs e)
        {

            bool isFailed = true;
            while (isFailed)
            {

                isFailed = await PrintDocViewer();

                // Meldung ausgeben
                DialogMessageBox msg = DialogMessageBox.GetInstance(this,
                    Title: MainServiceHelper.GetString("MainWindow_Action_PrintClose"),
                    Message: MainServiceHelper.GetString("MainWindow_ErrorExport_PrintClose_Msg"),
                    MessageIcon: MessageBoxImage.Question,
                    PositiveAction: new(MainServiceHelper.GetString("Common_Button_Retry"), () => { }),
                    NegativeAction: new(MainServiceHelper.GetString("Common_Button_Cancel"), () => { }));

                // Wenn Fehler aufgetreten ist, wird nicht beendet > Abbruch
                if (isFailed && MainServiceHelper.ShowDialog(overlayDialog, msg) == false) { return; }

            }
            MinimizeToTray();

        }

        // ########################################################################################

        private void BtnExportPrivate_Click(object sender, RoutedEventArgs e)
        {
            PrivateModeLogin();
        }
        private void BtnLogoutPrivate_Click(object sender, RoutedEventArgs e)
        {
            PrivateModeLogout();
        }

        // ########################################################################################

        private async void BtnActionPrintPrivate_Click(object sender, RoutedEventArgs e)
        {

            bool isFailed = await PrintDocViewer();

            // Meldung ausgeben
            DialogMessageBox msg = DialogMessageBox.GetInstance(this,
                Title: MainServiceHelper.GetString("MainWindow_Action_PrintPrivate"),
                Message: (isFailed ? MainServiceHelper.GetString("MainWindow_ErrorExport_Private_Print_Msg") : MainServiceHelper.GetString("MainWindow_ExportSuccess_Private_Print_Msg")),
                MessageIcon: (isFailed ? MessageBoxImage.Error : MessageBoxImage.Information),
                new(MainServiceHelper.GetString("Common_Button_Ok"), () => { }));
            MainServiceHelper.ShowDialog(overlayDialog, msg);

        }
        private async void BtnActionSavePrivate_Click(object sender, RoutedEventArgs e)
        {

            if (PrivateMasterData == null || PrivateMasterData.IsFailed) { return; }
            DateTime monthDate = monthInput.DisplayDate;

            ShowProgress(ProgressText: MainServiceHelper.GetString("MainWindow_Progress_Saving"));
            await Task.Delay(500);

            bool isFailed = true;

            try
            {

                // SpeicherDialog anzeigen
                SaveFileDialog saveFileDialog = new();
                saveFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                saveFileDialog.Filter = "PDF-Datei (*.pdf)|*.pdf";
                saveFileDialog.Title = "Dienstplan speichern";
                saveFileDialog.OverwritePrompt = true;
                saveFileDialog.AddExtension = true;
                saveFileDialog.FileName = string.Format("dienstplan-{0}-{1}.pdf", monthDate.ToString("MMMM_yyyy").ToLower(), PrivateMasterData.LastName.ToLower());
                if (saveFileDialog.ShowDialog() == true)
                {

                    // PDF speichern
                    if (CreatePdf(saveFileDialog.FileName)) { isFailed = false; }

                }

            }
            catch (Exception ex)
            {
                AppLog.Error(ex);
            }

            // Meldung ausgeben
            DialogMessageBox msg = DialogMessageBox.GetInstance(this,
                Title: MainServiceHelper.GetString("MainWindow_Action_SavePrivate"),
                Message: (isFailed ? MainServiceHelper.GetString("MainWindow_ErrorExport_Private_Save_Msg") : MainServiceHelper.GetString("MainWindow_ExportSuccess_Private_Save_Msg")),
                MessageIcon: (isFailed ? MessageBoxImage.Error : MessageBoxImage.Information),
                new(MainServiceHelper.GetString("Common_Button_Ok"), () => { }));
            MainServiceHelper.ShowDialog(overlayDialog, msg);

            HideProgress();

        }
        private async void BtnActionSaveIcal_Click(object sender, RoutedEventArgs e)
        {

            if (PrivateMasterData == null || PrivateMasterData.IsFailed) { return; }
            DateTime monthDate = monthInput.DisplayDate;

            ShowProgress(ProgressText: MainServiceHelper.GetString("MainWindow_Progress_Saving"));
            await Task.Delay(500);

            bool isFailed = true;

            try
            {

                // SpeicherDialog anzeigen
                SaveFileDialog saveFileDialog = new()
                {
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    Filter = "Kalenderdatei (*.ics)|*.ics",
                    Title = "Kalenderdatei speichern",
                    OverwritePrompt = true,
                    AddExtension = true,
                    FileName = string.Format("dienstplan-{0}{1}-{2}.ics", PrivateMasterData.LastName.ToLower(), PrivateMasterData.FirstName.ToLower(), DateTime.Now.ToString("yyyyMMdd"))
                };
                if (saveFileDialog.ShowDialog() == true)
                {

                    // Ical-Text schreiben
                    string content = CreateIcalText(monthDate);
                    if (content != "")
                    {
                        await File.WriteAllTextAsync(saveFileDialog.FileName, content);
                        isFailed = false;
                    }


                }

            }
            catch (Exception ex)
            {
                AppLog.Error(ex);
            }

            // Meldung ausgeben
            DialogMessageBox msg = DialogMessageBox.GetInstance(this,
                Title: MainServiceHelper.GetString("MainWindow_Action_IcalSave"),
                Message: (isFailed ? MainServiceHelper.GetString("MainWindow_ErrorExport_Private_Save_Msg") : MainServiceHelper.GetString("MainWindow_ExportSuccess_Private_Save_Msg")),
                MessageIcon: (isFailed ? MessageBoxImage.Error : MessageBoxImage.Information),
                new(MainServiceHelper.GetString("Common_Button_Ok"), () => { }));
            MainServiceHelper.ShowDialog(overlayDialog, msg);

            HideProgress();

        }
        private async void BtnActionSendPrivate_Click(object sender, RoutedEventArgs e)
        {

            if (PrivateMasterData == null || PrivateMasterData.IsFailed) { return; }
            DateTime monthDate = monthInput.DisplayDate;

            // Ziel-Email-Adresse
            DialogInputBox emailInput = DialogInputBox.GetInstance(this,
                Message: MainServiceHelper.GetString("MainWindow_Export_Private_Send_InputMsg"),
                TextLabel: MainServiceHelper.GetString("MainWindow_Export_Private_Send_InputLabel"),
                Validation: DialogInputBoxValidation.EMAIL);
            if (MainServiceHelper.ShowDialog(overlayDialog, emailInput) != true) { return; }
            string targetEmail = DialogInputBox.SelectedInputText;

            // TmpPfade
            bool isFailed = true;
            string tmpPdfPath = MainServiceHelper.GetTmpPdfPath(string.Format("dp-{0}.pdf", monthDate.ToString("MMM-yyyy").ToLower()));
            string tmpIcalPath = MainServiceHelper.GetTmpPdfPath(string.Format("dp-{0}.ics", monthDate.ToString("MMM-yyyy").ToLower()));

            try
            {

                ShowProgress(ProgressText: MainServiceHelper.GetString("MainWindow_Action_SendPrivate"));
                await Task.Delay(500); // Durch das Interop wird zeitweise der UI-Thread blockiert ... Damit trotzdem die StatusTexte aktualisiert werden, muss gewartet werden

                // Pdf erstellen
                if (CreatePdf(tmpPdfPath))
                {

                    // Ical-Text schreiben
                    string content = CreateIcalText(monthDate);
                    if (content != "")
                    {
                        await File.WriteAllTextAsync(tmpIcalPath, content);
                        if (File.Exists(tmpIcalPath))
                        {

                            await Task.Delay(500);

                            // E-Mail per Outlook versenden TODO: Outlook als ComVerweis hinzufügen
                            Microsoft.Office.Interop.Outlook.Application oApp = new();

                            Microsoft.Office.Interop.Outlook.MailItem mailItem = (Microsoft.Office.Interop.Outlook.MailItem)oApp.CreateItem(Microsoft.Office.Interop.Outlook.OlItemType.olMailItem);

                            mailItem.Subject = string.Format("Dienstplan {0}", monthDate.ToString("MMMM yyyy"));

                            mailItem.Attachments.Add(tmpIcalPath);
                            mailItem.Attachments.Add(tmpPdfPath);

                            mailItem.To = targetEmail;
                            mailItem.HTMLBody = string.Format(MainServiceHelper.GetString("MainWindow_Export_Private_Send_EmailText"), monthDate.ToString("MMM yyyy"), PrivateMasterData.LastName, PrivateMasterData.FirstName);

                            mailItem.Importance = Microsoft.Office.Interop.Outlook.OlImportance.olImportanceNormal;
                            mailItem.DeleteAfterSubmit = true;

                            mailItem.Send();

                            isFailed = false;

                        }

                    }

                }

            }
            catch (Exception ex)
            {
                AppLog.Error(ex);
            }

            await Task.Delay(500);

            // Meldung ausgeben
            DialogMessageBox msg = DialogMessageBox.GetInstance(this,
                Title: MainServiceHelper.GetString("MainWindow_Action_SendPrivate"),
                Message: (isFailed ? MainServiceHelper.GetString("MainWindow_ErrorExport_Private_Send_Msg") : MainServiceHelper.GetString("MainWindow_ExportSuccess_Private_Send_Msg")),
                MessageIcon: (isFailed ? MessageBoxImage.Error : MessageBoxImage.Information),
                new(MainServiceHelper.GetString("Common_Button_Ok"), () => { }));
            MainServiceHelper.ShowDialog(overlayDialog, msg);

            HideProgress();

        }

        // ########################################################################################

        private void DockPanel_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {

            // Service-Menü >> Wenn dreimal Rechtsklick auf Statusleiste
            if (e.ClickCount >= 3)
            {

                DialogAdminMenu menu = DialogAdminMenu.GetInstance(this, Service);
                MainServiceHelper.ShowDialog(overlayDialog, menu);

                UpdateDate();

            }

        }

        #endregion

    }

}
