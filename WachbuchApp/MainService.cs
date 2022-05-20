using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WachbuchApp
{

    internal class MainService
    {

        private readonly MainServiceConfiguration conf;
        private readonly MainServiceDatabase db;

        private readonly VivendiApi api;

        // ########################################################################################

        public MainServiceConfiguration Configuration => conf;
        public MainServiceDatabase Database => db;

        // ########################################################################################

        public MainService()
        {

            // Konfiguration & Datenbank laden
            conf = MainServiceConfiguration.LoadInstance();
            db = MainServiceDatabase.LoadInstance();

            // Vivendi-API vorbereiten
            api = new(Configuration.AnonymousUsers);

            // BackgroundFetcher starten
            RunBackgroundFetch();

        }

        // ########################################################################################

        #region BackgroundFetchService

        public void RunBackgroundFetch()
        {

            // Variablen initialisieren
            DateTime scheduledClean = DateTime.Now;

            // Timer erstellen
            Timer backgroundTimer = new(async (s) =>
            {

                try
                {

                    // Verbinden
                    var response = await api.FetchPublicFromTo(DateTime.Now, DateTime.Now.AddDays(31));
                    if (response.IsFailed)
                    {

                        // Wenn die Anmeldedaten nicht stimmen, kann dieser BackgroundFetcher beendet werden
                        // bis jemand das Programm öffnet und sich anmeldet. TODO: StateObjekt aktualisieren & ignorieren
                        //if (response.FetchState == VivendiApiState.CONNECTION_ERROR) { break; }

                        // Alle anderen Fehler (Internet fehlt, Serverfehler) werden ignoriert und in 2.5h erneut probiert

                    }
                    else
                    {

                        // Wenn erfolgreich abgerufen & Bekannte Schichten aktualisieren
                        db.ImportFetchPublic(response);
                        response.Shifts.ForEach(x => conf.AddKnownShift(x));
                        conf.SaveInstance();

                    }

                    // Datenbank bereinigen
                    if (scheduledClean < DateTime.Now)
                    {
                        db.CleanPublicCache();
                        scheduledClean = DateTime.Now.AddDays(3);
                    }

                }
                catch (Exception ex)
                {
                    AppLog.Error(ex);
                }

            }, null, TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));

        }

        #endregion

        // ############################################################################################

        #region Ereignisse

        public delegate void GetDataFinishedEventHandler(DateTime queryDate, MainServiceFetchDataState state);
        public event GetDataFinishedEventHandler? GetDataFinished;

        #endregion

        #region Abfragen

        public async Task<MainServiceFetchDataState> GetPublicData(DateTime date)
        {

            bool dataAvailable = db.TestDate(date);
            bool dataOutdated = db.IsDateDataOutdated(date);

            // Wenn Daten nicht vorhanden, bei Vivendi laden
            if (!dataAvailable || dataOutdated) { 

                var response = await api.FetchPublicFromTo(date, date);
                if (response.IsFailed)
                {

                    MainServiceFetchDataState state = new(response.FetchState, dataAvailable, db.GetDateDataLatestFetch(date));
                    GetDataFinished?.Invoke(date, state);
                    return state;

                }
                else
                {

                    // Wenn erfolgreich abgerufen & Bekannte Schichten aktualisieren
                    db.ImportFetchPublic(response);
                    response.Shifts.ForEach(x => conf.AddKnownShift(x));
                    conf.SaveInstance();

                }

            }

            // Daten zurückgeben
            MainServiceFetchDataState successState = new();
            GetDataFinished?.Invoke(date, successState);
            return successState;

        }

        public async Task<MainServiceFetchDataState> GetPrivateData(DateTime date, long privateId)
        {

            DateTime dateFrom = new(date.Year, date.Month, 1);
            DateTime dateTo = new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));

            // Öffentliche Daten prüfen & ggf. laden
            bool publicDataAvailable = true;
            bool publicDataOutdated = true;
            for (int day = 1; day <= dateTo.Day; day++)
            {
                DateTime thisDate = new(dateFrom.Year, dateFrom.Month, day);
                if (!db.TestDate(thisDate)) { publicDataAvailable = false; break; }
                if (!db.IsDateDataOutdated(thisDate)) { publicDataOutdated = false; break; }
            }
            if (!publicDataAvailable || publicDataOutdated)
            {

                var response = await api.FetchPublicFromTo(dateFrom, dateTo);
                if (response.IsFailed)
                {

                    // Wenn öffentlich keine Daten, dann hier bereits abbrechen
                    if (!publicDataAvailable)
                    {
                        MainServiceFetchDataState state = new(response.FetchState, false, DateTime.MinValue);
                        GetDataFinished?.Invoke(date, state);
                        return state;
                    }

                }
                db.ImportFetchPublic(response);

            }

            // Private Daten laden
            bool privateDataAvailable = true;
            bool privateDataOutdated = true;
            for (int day = 1; day <= dateTo.Day; day++)
            {
                DateTime thisDate = new(dateFrom.Year, dateFrom.Month, day);
                if (!db.TestPrivateDate(thisDate)) { privateDataAvailable = false; break; }
                if (!db.IsPrivateDateDataOutdated(thisDate)) { privateDataOutdated = false; break; }
            }
            if (!privateDataAvailable || privateDataOutdated)
            {

                var response = await api.FetchPrivateFromTo(dateFrom, dateTo, privateId);
                if (response.IsFailed)
                {

                    // Keine privaten Daten > Keine privaten Pläne
                    MainServiceFetchDataState state = new(response.FetchState, privateDataAvailable, db.GetDateDataLatestFetch(date));
                    GetDataFinished?.Invoke(date, state);
                    return state;

                }
                db.ImportFetchPrivate(response);

            }

            // Daten zurückgeben
            MainServiceFetchDataState successState = new();
            GetDataFinished?.Invoke(date, successState);
            return successState;

        }

        public async Task<VivendiApiMasterDataResponse> GetPrivateMasterData()
        {

            var response = await api.FetchPrivateMasterData();
            return response;

        }

        #endregion

        #region Methoden

        public async Task<VivendiApiState> SetNewCredentials(string username, string password)
        {
            string hash = MainServiceHelper.GetCryptoPasshash(password);
            var result = await api.TestLogin(username, hash);
            return result;
        }

        #endregion

    }

    #region MainService-Erweiterungen

    internal class MainServiceConfiguration
    {

        public CredentialBlock AnonymousUsers;

        public List<Book> Books;
        public List<string> KnownShifts;

        // ########################################################################################

        public MainServiceConfiguration()
        {

            AnonymousUsers = new();

            Books = new();
            KnownShifts = new();

        }

        public void ResetConfiguration()
        {

            // Anonyme Anmeldedaten zurücksetzen
            AnonymousUsers = new();

            // Wachbücher zurücksetzen
            Books.Add(
               new Book("RW Coswig", "docWachbuchCoswig.html", "doc-date",

                        new List<BookVehicle>() { new BookVehicle("Jh Cos 41/83-1", "MEI-MH 834"),
                                                   new BookVehicle("Jh Cos 41/83-2", "MEI-MH 835"),
                                                   new BookVehicle("Jh Cos 41/83-3", "DD-MH 831"),
                                                   new BookVehicle("Jh Mei 41/83-4", "DD-MH 8304"),
                                                   new BookVehicle("Jh Mei 41/83-5", "DD-MH 8305")},

                        new List<BookShift>() { new BookShift("#R1T-Co#4340#", "Jh Cos 41/83-1", null, "rtw1-funk", "rtw1-keyplate", "rtw1-times", "rtw1-emp1", "rtw1-emp2", "rtw1-empH"),
                                                 new BookShift("#R2T-Co#4342#", "Jh Cos 41/83-2", null, "rtw2-funk", "rtw2-keyplate", "rtw2-times", "rtw2-emp1", "rtw2-emp2", "rtw2-empH"),
                                                 new BookShift("#R1N-Co#4341#", "Jh Cos 41/83-1", null, "nrtw1-funk", "nrtw1-keyplate", "nrtw1-times", "nrtw1-emp1", "nrtw1-emp2", "nrtw1-empH")}));

            Books.Add(
                new Book("RW Meißen", "docWachbuchMeissen.html", "doc-date",

                         new List<BookVehicle>() { new BookVehicle("Jh Mei 41/83-1", "MEI-MH 831"),
                                                   new BookVehicle("Jh Mei 41/83-2", "MEI-MH 832"),
                                                   new BookVehicle("Jh Mei 41/83-3", "MEI-MH 833"),
                                                   new BookVehicle("Jh Mei 41/83-4", "DD-MH 8304"),
                                                   new BookVehicle("Jh Mei 41/83-5", "DD-MH 8305"),
                                                   new BookVehicle("Jh Cos 41/83-3", "DD-MH 831"),

                                                   new BookVehicle("Jh Mei 41/85-1", "MEI-MH 850"),
                                                   new BookVehicle("Jh Mei 41/85-2", "MEI-RK 853"),
                                                   new BookVehicle("Jh Mei 41/85-3", "MEI-MH 853"),
                                                   new BookVehicle("Jh Mei 41/85-4", "DD-MH 8504"),
                                                   new BookVehicle("Jh Mei 41/85-5", "DD-MH 8508")},

                         new List<BookShift>() { new BookShift("#R1T#4334#", "Jh Mei 41/83-1", null, "rtw1-funk", "rtw1-keyplate", "rtw1-times", "rtw1-emp1", "rtw1-emp2", "rtw1-empH"),
                                                 new BookShift("#R2T#4337#", "Jh Mei 41/83-2", null, "rtw2-funk", "rtw2-keyplate", "rtw2-times", "rtw2-emp1", "rtw2-emp2", "rtw2-empH"),
                                                 new BookShift("#R3T#4339#", "Jh Mei 41/83-3", "rtw3-empty", "rtw3-funk", "rtw3-keyplate", "rtw3-times", "rtw3-emp1", "rtw3-emp2", "rtw3-empH"),
                                                 new BookShift("#K1#4336#", "Jh Mei 41/85-1", "ktw1-empty", "ktw1-funk", "ktw1-keyplate", "ktw1-times", "ktw1-emp1", "ktw1-emp2", "ktw1-empH"),
                                                 new BookShift("#K2#4343#", "Jh Mei 41/85-2", "ktw2-empty", "ktw2-funk", "ktw2-keyplate", "ktw2-times", "ktw2-emp1", "ktw2-emp2", "ktw2-empH"),
                                                 new BookShift("#K3#4344#", "Jh Mei 41/85-3", "ktw3-empty", "ktw3-funk", "ktw3-keyplate", "ktw3-times", "ktw3-emp1", "ktw3-emp2", "ktw3-empH"),

                                                 new BookShift("#R1N#4335#", "Jh Mei 41/83-1", null, "nrtw1-funk", "nrtw1-keyplate", "nrtw1-times", "nrtw1-emp1", "nrtw1-emp2", "nrtw1-empH"),
                                                 new BookShift("#R2N#4338#", "Jh Mei 41/83-2", null, "nrtw2-funk", "nrtw2-keyplate", "nrtw2-times", "nrtw2-emp1", "nrtw2-emp2", "nrtw2-empH"),

                                                 new BookShift("#RB-T#4332#", null, null, null, null, null, "rbt-emp1", "rbt-emp2", "rbt-empH"),
                                                 new BookShift("#RB-N#5337#", null, null, null, null, null, "rbn-emp1", "rbn-emp2", "rbn-empH")}));

            Books.Add(
                new Book("NEF Meißen", "docWachbuchNefMeissen.html", "doc-date",

                         new List<BookVehicle>() { new BookVehicle("Jh Mei 41/82-1", "MEI-RK 182"),
                                                   new BookVehicle("Jh Mei 41/82-2", "DD-MH 822")},

                         new List<BookShift>() { new BookShift("#NT#4330#", "Jh Mei 41/82-1", null, "nef1-funk", "nef1-keyplate", "nef1-times", "nef1-emp1", "nef1-empH", null),
                                                 new BookShift("#NN#4331#", "Jh Mei 41/82-1", null, "nnef1-funk", "nnef1-keyplate", "nef1-times", "nnef1-emp1", "nnef1-empH", null)}));

            // Bekannte Schichten löschen
            KnownShifts = new();

        }

        // ########################################################################################

        private static string SAVEPATH => MainServiceHelper.GetDbPath("configuration.json");

        public static MainServiceConfiguration LoadInstance()
        {

            // Config aus Json deserialisieren
            MainServiceConfiguration? result = null;
            try
            {
                using var handle = new StreamReader(SAVEPATH);
                string json = handle.ReadToEnd();
                result = JsonConvert.DeserializeObject<MainServiceConfiguration>(json, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto });
            }
            catch (Exception e)
            {
                AppLog.Error(e.Message, e.StackTrace);
            }

            // Wenn keine Config, oder nicht geladen aus unbekannten Gründen > Reset
            if (result == null)
            {
                result = new();
                result.ResetConfiguration();
                result.SaveInstance();
            }
            return result;
        }

        public void SaveInstance()
        {

            string json = JsonConvert.SerializeObject(this, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto, Formatting = Formatting.Indented });
            try
            {
                using var handle = new StreamWriter(SAVEPATH, false);
                handle.Write(json);
            }
            catch (Exception e)
            {
                AppLog.Error(e.Message, e.StackTrace);
            }

        }

        // ########################################################################################

        public void AddKnownShift(MainServiceDatabase.Shift s)
        {
            if (!KnownShifts.Contains(s.ConfigKey)) { KnownShifts.Add(s.ConfigKey); }
        }

        // ########################################################################################

        public class Book
        {

            public string StationName { get; set; }
            public string DocFile { get; set; }
            public string LabelDate { get; set; }

            public List<BookVehicle> Vehicles { get; set; }
            public List<BookShift> Shifts { get; set; }

            // ########################################################################################

            public Book(string stationName, string docFilename, string docLabelDate, List<BookVehicle> vehicleList, List<BookShift> shiftsList)
            {
                StationName = stationName;
                DocFile = docFilename;
                LabelDate = docLabelDate;
                Vehicles = vehicleList;
                Shifts = shiftsList;
            }

        }

        public class BookVehicle : IEquatable<BookVehicle>
        {

            public string FunkId { get; set; }
            public string Keyplate { get; set; }

            public BookVehicle(string funkId, string keyplate)
            {
                FunkId = funkId;
                Keyplate = keyplate;
            }

            bool IEquatable<BookVehicle>.Equals(BookVehicle? other)
            {
                if (other == null) { return false; }
                return FunkId == other.FunkId && Keyplate == other.Keyplate;
            }

            public override bool Equals(object? obj)
            {
                return ((IEquatable<BookVehicle>)this).Equals(obj as BookVehicle);
            }

            public override int GetHashCode()
            {
                return (FunkId+Keyplate).GetHashCode();
            }

        }

        public class BookShift
        {

            public string ConfigKey { get; set; }

            [JsonIgnore]
            public long VivendiId
            {
                get
                {
                    // "#asdas#1231#"
                    if (string.IsNullOrWhiteSpace(ConfigKey) ||
                        !ConfigKey.Contains('#')) { return -1; }
                    var split = ConfigKey.Split('#');
                    if (split.Length == 4)
                    {
                        if (long.TryParse(split[2], out long result)) { return result; }
                    }
                    return -1;

                }
            }

            [JsonIgnore]
            public string LabelVehicleKey => LabelFunk == null ? "" : LabelFunk.Split('-')[0];

            public string? DefaultVehicle { get; set; }

            public string? LabelEmpty { get; set; }
            public string? LabelFunk { get; set; }
            public string? LabelKeyplate { get; set; }
            public string? LabelTimes { get; set; }
            public string? LabelEmp1 { get; set; }
            public string? LabelEmp2 { get; set; }
            public string? LabelEmpH { get; set; }

            public BookShift(string configKey, string? defaultVehicle, string? labelEmpty, string? labelFunk, string? labelKeyplate, string? labelTimes, string? labelEmp1, string? labelEmp2, string? labelEmpH)
            {
                ConfigKey = configKey;
                DefaultVehicle = defaultVehicle;
                LabelEmpty = labelEmpty;
                LabelFunk = labelFunk;
                LabelKeyplate = labelKeyplate;
                LabelTimes = labelTimes;
                LabelEmp1 = labelEmp1;
                LabelEmp2 = labelEmp2;
                LabelEmpH = labelEmpH;
            }

        }

        // ########################################################################################

        public class CredentialBlock
        {

            [JsonProperty(PropertyName = "Block")]
            private readonly Dictionary<string, Credential> _block;

            // ########################################################################################

            public CredentialBlock()
            {
                _block = new();
            }

            // ########################################################################################

            [JsonIgnore]
            public List<Credential> Credentials => (from x in _block where x.Value.IsAvailable orderby x.Value.LastRenewed descending select x.Value).ToList();

            // ########################################################################################

            public void AddCredentials(string username, string passhash)
            {
                if (_block.ContainsKey(username))
                {
                    _block[username].Enable(passhash);
                }
                else
                {
                    _block.Add(username, new(username, passhash));
                }
            }

            public void RemoveCredential(string username)
            {
                if (_block.ContainsKey(username))
                {
                    _block[username].Disable();
                }
            }

        }
        public class Credential
        {

            public string Username { get; set; }
            public string? Passhash { get; set; }
            public DateTime LastRenewed { get; set; }

            [JsonIgnore]
            public bool IsAvailable => Passhash != null;

            public Credential(string user, string passhash)
            {
                Username = user;
                Passhash = passhash;
                LastRenewed = DateTime.MinValue;
            }

            public void Disable()
            {
                Passhash = null;
            }
            public void Enable(string passhash)
            {
                Passhash = passhash;
                LastRenewed = DateTime.Now;
            }

        }

    }

    internal class MainServiceDatabase
    {

        public class Employee
        {

            public enum EmployeeQualification
            {
                Azubi,
                RH,
                RS,
                UNKNOWN,
                RA,
                NFS,
                NA
            }

            // ####################################################################################

            [JsonProperty(PropertyName = "vId")]
            public long VivendiId { get; set; }

            [JsonProperty(PropertyName = "eFN")]
            public string FirstName { get; set; }

            [JsonProperty(PropertyName = "eLN")]
            public string LastName { get; set; }

            [JsonProperty(PropertyName = "eQu")]
            public EmployeeQualification Qualification { get; set; }

            // ####################################################################################

            public Employee(long vivendiId, string firstName, string lastName)
            {
                VivendiId = vivendiId;
                FirstName = firstName;
                LastName = lastName;
                Qualification = EmployeeQualification.UNKNOWN;
            }

            // ####################################################################################

            [JsonIgnore]
            public string EmployeeLabelText => string.Format("{0}, {1} {2}", LastName, FirstName, QualificationTextShort);

            [JsonIgnore]
            public string EmployeeNameText => string.Format("{0} {1}", FirstName, LastName);

            // ####################################################################################

            [JsonIgnore]
            public string QualificationTextShort => MainServiceHelper.GetQualificationTextShort(Qualification);

            [JsonIgnore]
            public string QualificationTextFull => MainServiceHelper.GetQualificationTextFull(Qualification);

        }

        public class Shift
        {

            [JsonIgnore]
            public string PrimaryKey => string.Format("#{0}{1}", MainServiceHelper.ConvertDateOnly(ShiftDate), ConfigKey);

            [JsonIgnore]
            public string ConfigKey => string.Format("#{0}#{1}#", ShortName, VivendiId);

            // ####################################################################################

            [JsonProperty(PropertyName = "vId")]
            public long VivendiId { get; set; }

            [JsonProperty(PropertyName = "sFu")]
            public string FullName { get; set; }

            [JsonProperty(PropertyName = "sSh")]
            public string ShortName { get; set; }

            [JsonProperty(PropertyName = "sDt")]
            public DateTime ShiftDate { get; set; }

            [JsonProperty(PropertyName = "tSt")]
            public DateTime TimeStart { get; set; }

            [JsonProperty(PropertyName = "tEn")]
            public DateTime TimeEnd { get; set; }

            [JsonProperty(PropertyName = "tPs")]
            public TimeSpan TimePause { get; set; }

            [JsonProperty(PropertyName = "bEm")]
            public List<long> BoundEmployee { get; set; }

            [JsonProperty(PropertyName = "meDa")]
            public DateTime FetchedDateTime { get; set; }

            // ####################################################################################

            public Shift()
            {

                VivendiId = -1;
                FullName = "";
                ShortName = "";
                ShiftDate = DateTime.MinValue;
                TimeStart = DateTime.MinValue;
                TimeEnd = DateTime.MinValue;
                TimePause = TimeSpan.Zero;
                BoundEmployee = new();
                FetchedDateTime = DateTime.MinValue;

            }

            public Shift(long vivendiId, string fullname, string shortname, DateTime shiftDate, DateTime timeStart, DateTime timeEnd, TimeSpan timePause, long boundEmployee)
            {

                VivendiId = vivendiId;
                FullName = fullname;
                ShortName = shortname;
                ShiftDate = shiftDate;
                TimeStart = timeStart;
                TimeEnd = timeEnd;
                TimePause = timePause;

                if (BoundEmployee == null) { BoundEmployee = new(); }
                if (!BoundEmployee.Contains(boundEmployee)) { BoundEmployee.Add(boundEmployee); }

                FetchedDateTime = DateTime.Now;

            }

        }

        // ############################################################################################

        [JsonProperty(PropertyName = "eL")]
        private readonly ConcurrentDictionary<long, Employee> employeeDictionary;

        [JsonProperty(PropertyName = "sL")]
        private readonly ConcurrentDictionary<string, Shift> shiftDictionary;

        [JsonIgnore]
        private readonly ConcurrentDictionary<string, Shift> privateShifts;

        // ############################################################################################

        public MainServiceDatabase()
        {

            employeeDictionary = new();
            shiftDictionary = new();
            privateShifts = new();

        }

        // ########################################################################################

        private static string SAVEPATH => MainServiceHelper.GetDbPath("data.db");

        public static MainServiceDatabase LoadInstance()
        {

            MainServiceDatabase? result = null;

            // Database aus Json deserialisieren
            if (System.IO.File.Exists(SAVEPATH))
            {

                try
                {

                    string json = MainServiceHelper.ReadEncryptedFile(SAVEPATH);
                    result = JsonConvert.DeserializeObject<MainServiceDatabase>(json, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None });

                }
                catch (Exception e)
                {
                    AppLog.Error(e.Message, e.StackTrace);
                }

            }

            // Wenn keine Datenbank, oder nicht geladen aus unbekannten Gründen > Reset
            if (result == null)
            {
                result = new();
                result.SaveInstance();
            }
            return result;
        }

        public void SaveInstance()
        {

            string json = JsonConvert.SerializeObject(this, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None, Formatting = Formatting.None });
            MainServiceHelper.WriteEncryptedFile(json, SAVEPATH);

        }

        // ############################################################################################

        public bool TestDate(DateTime date)
        {
            var query = (from x in shiftDictionary.Values where x.ShiftDate.Date == date.Date select x);
            return query.Any();
        }

        public bool IsDateDataOutdated(DateTime date)
        {
            var query = (from x in shiftDictionary.Values where (x.ShiftDate.Date == date.Date) && ((DateTime.Now - x.FetchedDateTime).TotalHours > 6) select x);
            return query.Any();
        }

        public DateTime GetDateDataLatestFetch(DateTime date)
        {
            var query = (from x in shiftDictionary.Values where (x.ShiftDate.Date == date.Date) orderby x.FetchedDateTime ascending select x.FetchedDateTime).FirstOrDefault(DateTime.MinValue);
            return query;
        }

        // ############################################################################################

        public bool TestPrivateDate(DateTime date)
        {
            var query = (from x in privateShifts.Values where x.ShiftDate.Date == date.Date select x);
            return query.Any();
        }

        public bool IsPrivateDateDataOutdated(DateTime date)
        {
            var query = (from x in privateShifts.Values where (x.ShiftDate.Date == date.Date) && ((DateTime.Now - x.FetchedDateTime).TotalHours > 6) select x);
            return query.Any();
        }

        public DateTime GetPrivateDateDataLatestFetch(DateTime date)
        {
            var query = (from x in privateShifts.Values where (x.ShiftDate.Date == date.Date) orderby x.FetchedDateTime ascending select x.FetchedDateTime).FirstOrDefault(DateTime.MinValue);
            return query;
        }

        public void CleanPublicCache()
        {

            DateTime obsoleteBorder = DateTime.Now.AddDays(-70);

            // Schichten entfernen
            var toRemoveShifts = from x in shiftDictionary where x.Value.ShiftDate < obsoleteBorder select x.Key;
            if (toRemoveShifts.Any())
            {
                toRemoveShifts.ToList().ForEach(x => shiftDictionary.Remove(x, out _));
            }

        }

        // ############################################################################################

        public List<Employee> GetBoundEmployee(Shift? shift)
        {
            if (shift == null) { return new(); }
            var result = (from x in employeeDictionary.Values where shift.BoundEmployee.Contains(x.VivendiId) orderby x.Qualification descending select x).ToList(); //.Keys.Contains(x.VivendiId) orderby x.Qualification descending select x).ToList();
            return result;
        }

        public Employee? GetBuddyEmployee(Shift? shift, long privateId)
        {

            var otherEmp = (from x in GetBoundEmployee(shift) where x.VivendiId != privateId orderby x.Qualification descending select x);
            if (otherEmp.Any()) { return otherEmp.First(); }
            else { return null; }

        }

        public Shift? GetShift(DateTime date, string shiftConfigKey)
        {
            var queryShift = (from x in shiftDictionary.Values where (x.ShiftDate.Date == date.Date) && (x.ConfigKey == shiftConfigKey) select x).FirstOrDefault();
            return queryShift;
        }

        public Employee? GetEmployee(long employeeId)
        {
            if (!employeeDictionary.ContainsKey(employeeId)) { return null; }
            return employeeDictionary[employeeId];
        }

        // ############################################################################################

        [JsonIgnore]
        public List<long> GetUnknownEmployees => (from x in employeeDictionary where x.Value.Qualification == Employee.EmployeeQualification.UNKNOWN select x.Key).ToList();

        public void SetEmployeeQualification(long employeeId, Employee.EmployeeQualification newQualification)
        {
            if (!employeeDictionary.ContainsKey(employeeId)) { return; }
            employeeDictionary[employeeId].Qualification = newQualification;
            SaveInstance();
        }

        // ############################################################################################

        public void ClearPrivateCache()
        {
            privateShifts.Clear();
        }

        public List<Shift> GetPrivateShifts(DateTime monthDate)
        {
            return (from x in privateShifts.Values where x.ShiftDate.Year == monthDate.Year && x.ShiftDate.Month == monthDate.Month select x).ToList();
        }

        // ############################################################################################

        public void ImportFetchPublic(VivendiApiFetchResponse fetchResponse)
        {

            if (fetchResponse.IsFailed) { return; }

            // Mitarbeiter importieren
            foreach (var e in fetchResponse.Employee)
            {
                employeeDictionary.AddOrUpdate(e.VivendiId,
                                               e,
                                               (vivendiId, oldValue) =>
                                               {
                                                   oldValue.FirstName = e.FirstName;
                                                   oldValue.LastName = e.LastName;
                                                   return oldValue;
                                               });
            }

            // Schichten importieren
            foreach (var s in fetchResponse.Shifts)
            {
                shiftDictionary.AddOrUpdate(s.PrimaryKey, s, (primaryKey, oldValue) => s);
            }

            // Alte Schichten, die jetzt nicht mehr übertragen wurden, filtern TODO Optimieren
            List<string> toClean = new();
            DateTime cleanDate = new(fetchResponse.FetchedFrom.Year, fetchResponse.FetchedFrom.Month, fetchResponse.FetchedFrom.Day);
            while (cleanDate <= fetchResponse.FetchedTo)
            {

                var old = (from x in shiftDictionary.Values where x.ShiftDate.Date == cleanDate.Date && !(from y in fetchResponse.Shifts select y.PrimaryKey).ToList().Contains(x.PrimaryKey) select x.PrimaryKey);
                toClean.AddRange(old);

                cleanDate = cleanDate.AddDays(1);

            }
            toClean.ForEach((x) => { shiftDictionary.TryRemove(x, out Shift? value); });

            // Speichern
            SaveInstance();

        }

        public void ImportFetchPrivate(VivendiApiFetchResponse fetchResponse)
        {

            if (fetchResponse.IsFailed) { return; }

            // Schichten importieren
            foreach (var s in fetchResponse.Shifts)
            {
                privateShifts.AddOrUpdate(s.PrimaryKey, s, (primaryKey, oldValue) => s);
            }

        }

    }

    internal class MainServiceHelper
    {

        #region IO

        public static string GetDocPath(string docName)
        {
            return System.IO.Path.Combine(Environment.CurrentDirectory, "docs", docName);
        }

        public static string GetDbPath(string dbName)
        {
            string dir = System.IO.Path.Combine(Environment.CurrentDirectory, "data");
            if (!System.IO.Directory.Exists(dir)) { System.IO.Directory.CreateDirectory(dir); }
            return System.IO.Path.Combine(dir, dbName);
        }

        public static string GetTmpPdfPath(string FILENAME = "bookPrint.pdf")
        {
            string SAVEPATH = Path.Combine(Environment.CurrentDirectory, "tmp", FILENAME);
            if (!Directory.Exists(Path.GetDirectoryName(SAVEPATH))) { Directory.CreateDirectory(Path.GetDirectoryName(SAVEPATH)!); }
            return SAVEPATH;
        }

        #endregion
        #region DateTime

        public static string ConvertDateOnly(DateTime date)
        {
            return date.ToString("yyyy-MM-dd");
        }

        public static string ConvertSqlDateOnly(DateTime date)
        {
            return date.ToString("yyyy-MM-dd 00:00:00.000");
        }

        public static string ConvertSqlDateTime(DateTime dateTime)
        {
            return dateTime.ToString("yyyy-MM-dd HH:mm:ss.FFF");
        }

        public static string ConvertDateHumanReadable(DateTime dateTime)
        {
            TimeSpan diff = DateTime.Now - dateTime;
            string timeStr = dateTime.ToString("HH:mm");

            if (dateTime.Date == DateTime.Now.Date && diff.TotalMinutes < 3)
            {
                return "GERADE EBEN";
            }
            else if (dateTime.Date == DateTime.Now.Date && diff.TotalHours < 24)
            {
                return timeStr;
            }
            else if (dateTime.Date != DateTime.Now.Date && diff.TotalHours < 24)
            {
                return "GESTERN, " + timeStr;
            }
            else if (diff.TotalHours >= 24 && diff.TotalHours < 48)
            {
                return "VORGESTERN";
            }
            else if (diff.TotalHours >= 48 && dateTime.Date.Year == DateTime.Now.Date.Year)
            {
                return dateTime.ToString("ddd, dd. MMM");
            }
            else
            {
                return dateTime.ToString("ddd, dd. MMM yyyy");
            }
        }

        #endregion
        #region Network

        public static string GetUrlNonce => "?nonce=" + DateTime.UtcNow.Ticks.ToString();

        #endregion
        #region Cryptography

        public static string GetCryptoPasshash(string password)
        {

            try
            {
                const string secretKey = "HABh2b3czM4jhBXN3rfrMmWMXJVCMnLQTPYFmmdanKEFUgd6RzzvBXDWfyqgBVvq";

                var hash = new StringBuilder(); ;
                byte[] secretkeyBytes = Encoding.UTF8.GetBytes(secretKey);
                byte[] inputBytes = Encoding.UTF8.GetBytes(password);

                using var hmac = new System.Security.Cryptography.HMACSHA512(secretkeyBytes);
                byte[] hashValue = hmac.ComputeHash(inputBytes);
                return Convert.ToBase64String(hashValue);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex);
                return String.Empty;
            }

        }

        private static byte[] EncryptedFileKeyArray { 
            get
            {
                var defaultArray = new byte[32];
                var identityBytes = Encoding.UTF8.GetBytes(System.Security.Principal.WindowsIdentity.GetCurrent(System.Security.Principal.TokenAccessLevels.Read).User?.Value ?? MainServiceHelper.GetString("MainWindow_Title"));
                Array.Copy(identityBytes, defaultArray, 32);
                return defaultArray;
            } 
        }
        private static byte[] EncryptedFileInitVectorArray
        {
            get
            {
                var defaultArray = new byte[16];
                var identityBytes = Encoding.UTF8.GetBytes(System.Security.Principal.WindowsIdentity.GetCurrent(System.Security.Principal.TokenAccessLevels.Read).User?.Value ?? MainServiceHelper.GetString("MainWindow_Title")).Reverse().ToArray();
                Array.Copy(identityBytes, defaultArray, 16);
                return defaultArray;
            }
        }

        public static void WriteEncryptedFile(string content, string path)
        {

            // AES-Provider erstellen
            using Aes aes = Aes.Create();
            aes.Key = EncryptedFileKeyArray;
            aes.IV = EncryptedFileInitVectorArray;

            // Datei schreiben
            using FileStream file = new(path, FileMode.Create);
            using CryptoStream enc = new(file, aes.CreateEncryptor(), CryptoStreamMode.Write);
            using StreamWriter input = new(enc);
            input.Write(content);

        }

        public static string ReadEncryptedFile(string path)
        {

            // AES-Provider erstellen
            using Aes aes = Aes.Create();
            aes.Key = EncryptedFileKeyArray;
            aes.IV = EncryptedFileInitVectorArray;

            // Datei öffnen
            using FileStream file = new(path, FileMode.Open);
            using CryptoStream enc = new(file, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using StreamReader output = new(enc);
            return output.ReadToEnd();

        }

        #endregion
        #region HTML

        public static void SetHtmlInnerText(CefSharp.Wpf.ChromiumWebBrowser? host, string? htmlId, string? text)
        {
            if (host == null) { return; }
            if (string.IsNullOrEmpty(htmlId)) { return; }
            if (text == null) { text = ""; }

            CefSharp.WebBrowserExtensions.ExecuteScriptAsyncWhenPageLoaded(host, String.Format("document.getElementById('{0}').innerText='{1}';", htmlId, text));
        }
        public static void ClearHtmlInnerText(CefSharp.Wpf.ChromiumWebBrowser? host, string? htmlId)
        {
            SetHtmlInnerText(host, htmlId, "");
        }

        public static void SetHtmlMatchFontSizes(CefSharp.Wpf.ChromiumWebBrowser? host)
        {
            if (host == null) { return; }

            CefSharp.WebBrowserExtensions.ExecuteScriptAsyncWhenPageLoaded(host,
                "var inputs = document.getElementsByClassName('input');" +
                "for (var ii = 0; ii < inputs.length; ii++) {" +
                "var iiItObj = inputs[ii]; var iiFont = 3.4; iiItObj.style='font-size:' + iiFont.toString() + 'mm;';" +
                "while(iiItObj.scrollWidth>iiItObj.offsetWidth) { iiFont -= 0.1; iiItObj.style='font-size:' + iiFont.toString() + 'mm;'; }}");

        }

        public static void SetHtmlClassNoData(CefSharp.Wpf.ChromiumWebBrowser? host, string? htmlId)
        {
            if (host == null) { return; }
            if (string.IsNullOrEmpty(htmlId)) { return; }

            CefSharp.WebBrowserExtensions.ExecuteScriptAsyncWhenPageLoaded(host,
                "var cl = document.getElementsByClassName('" + htmlId + "');" +
                "for (var i = 0; i < cl.length; i++)" +
                "{ cl[i].className += ' nodata'; }");

        }
        public static void RemoveHtmlClassNoData(CefSharp.Wpf.ChromiumWebBrowser? host, string? htmlId)
        {
            if (host == null) { return; }
            if (string.IsNullOrEmpty(htmlId)) { return; }

            CefSharp.WebBrowserExtensions.ExecuteScriptAsyncWhenPageLoaded(host,
                "var cl = document.getElementsByClassName('" + htmlId + "');" +
                "for (var i = 0; i < cl.length; i++)" +
                "{ cl[i].className = cl[i].className.replace(' nodata', ''); }");

        }

        public static void SetHtmlDocMode(CefSharp.Wpf.ChromiumWebBrowser? host, bool PreviewMode = false, bool PrintMode = false)
        {
            if (host == null) { return; }
            if (!PreviewMode && !PrintMode) { return; }

            CefSharp.WebBrowserExtensions.ExecuteScriptAsyncWhenPageLoaded(host,
                "document.body.className='" + (PreviewMode ? "preview" : "") + "';");
        }

        #endregion
        #region Resources

        public static string GetString(string name)
        {
            string? local = (string?)System.Windows.Application.Current.TryFindResource(name);
            return local ?? "";
        }

        #endregion
        #region UI

        public static bool? ShowDialog(System.Windows.Controls.Grid overlay, System.Windows.Window message)
        {
            overlay.Visibility = System.Windows.Visibility.Visible;
            var result = message.ShowDialog();
            overlay.Visibility = System.Windows.Visibility.Collapsed;
            return result;
        }

        #endregion
        #region Convert 

        public static string GetQualificationTextShort(MainServiceDatabase.Employee.EmployeeQualification qualification)
        {
            return qualification switch
            {
                MainServiceDatabase.Employee.EmployeeQualification.RH => "(RH)",
                MainServiceDatabase.Employee.EmployeeQualification.RS => "(RS)",
                MainServiceDatabase.Employee.EmployeeQualification.RA => "(RA)",
                MainServiceDatabase.Employee.EmployeeQualification.NFS => "(NFS)",
                MainServiceDatabase.Employee.EmployeeQualification.NA => "(NA)",
                _ => ""
            };
        }

        public static string GetQualificationTextFull(MainServiceDatabase.Employee.EmployeeQualification qualification)
        {
            return qualification switch
            {
                MainServiceDatabase.Employee.EmployeeQualification.Azubi => "Azubi",
                MainServiceDatabase.Employee.EmployeeQualification.RH => "Rettungshelfer",
                MainServiceDatabase.Employee.EmployeeQualification.RS => "Rettungssanitäter",
                MainServiceDatabase.Employee.EmployeeQualification.RA => "Rettungsassistent",
                MainServiceDatabase.Employee.EmployeeQualification.NFS => "Notfallsanitäter",
                MainServiceDatabase.Employee.EmployeeQualification.NA => "Notarzt",
                _ => "Unbekannt"
            };
        }

        public class ExtensionIcal
        {

            private readonly StringBuilder content;

            public ExtensionIcal()
            {
                content = new();
                content.AppendLine("BEGIN:VCALENDAR");
                content.AppendLine("VERSION:2.0");
                content.AppendLine("PRODID:https://dienstplan.malteser.org");
            }

            public string GetIcal()
            {
                content.AppendLine("END:VCALENDAR");
                return content.ToString();
            }

            // ############################################################################################

            private static string ConvertDateTime(DateTime dateTime) { return dateTime.ToString(@"yyyyMMdd\THHmmss"); }

            // ############################################################################################

            public void AddShift(MainServiceDatabase.Shift shift, string? teamBuddy)
            {

                content.AppendLine("BEGIN:VEVENT");
                content.AppendLine(string.Format("UID:{0}@steiiin-cos-dp", shift.PrimaryKey));
                content.AppendLine(string.Format("DTSTAMP:{0}", ConvertDateTime(DateTime.Now)));
                content.AppendLine(string.Format("SUMMARY;CHARSET=UTF-8:{0}", shift.ShortName));
                content.AppendLine(string.Format("DESCRIPTION;CHARSET=UTF-8:{0}{1}", shift.FullName, teamBuddy == null ? "" : " (mit " + teamBuddy + ")"));
                if (teamBuddy != null) { content.AppendLine(string.Format("LOCATION;CHARSET=UTF-8:{0}", teamBuddy)); }
                content.AppendLine("CLASS:PUBLIC");
                content.AppendLine(string.Format("DTSTART:{0}", ConvertDateTime(shift.TimeStart)));
                content.AppendLine(string.Format("DTEND:{0}", ConvertDateTime(shift.TimeStart)));
                content.AppendLine("END:VEVENT");

            }

        }

        #endregion

    }

    #endregion
    #region MainService-AbfrageObjekte

    internal class MainServiceFetchDataState
    {

        public bool IsFailed => FetchState != VivendiApiState.SUCCESSFUL;
        public VivendiApiState FetchState { get; }

        public bool IsDataAvailable { get; }
        public DateTime AvailabeDataLastFetched { get; }

        public MainServiceFetchDataState(VivendiApiState errorType, bool dataAvailable, DateTime dataAvailableLastFetched)
        {
            FetchState = errorType; // Nur für Fehler, deshalb ein SUCCESSFUL hier abfangen
            if (errorType == VivendiApiState.SUCCESSFUL) { FetchState = VivendiApiState.SERVER_APP_ERROR; }
            IsDataAvailable = dataAvailable;
            AvailabeDataLastFetched = dataAvailableLastFetched;
        }

        public MainServiceFetchDataState()
        {
            FetchState = VivendiApiState.SUCCESSFUL;
            IsDataAvailable = true;
            AvailabeDataLastFetched = DateTime.Now;
        }

    }

    #endregion

    // ############################################################################################

    #region VivendiPep

    internal class VivendiApi
    {

        public VivendiApi(MainServiceConfiguration.CredentialBlock credentialBlock)
        {
            CredentialBlock = credentialBlock;
            NewConnection();
        }

        // ########################################################################################

        private HttpClient client = new();

        // ########################################################################################

        private readonly static Uri constEndpointBase = new("https://dienstplan.malteser.org/");

        private readonly Uri urlEndpointLogin = new(constEndpointBase, "api/selfservice/v1/user/login");
        private readonly Uri urlEndpointShiftPublic = new(constEndpointBase, "api/selfservice/v1/dienstplan/bereich/332/");
        private readonly Uri urlEndpointShiftPrivate = new(constEndpointBase, "api/selfservice/v1/dienstliste/");
        private readonly Uri urlEndpointEmployee = new(constEndpointBase, "api/selfservice/v1/stammdaten/mitarbeiter");

        private const int constFilterBereichId = 332;

        // ########################################################################################

        private readonly MainServiceConfiguration.CredentialBlock CredentialBlock;

        private void NewConnection()
        {
            client = new();
            client.BaseAddress = constEndpointBase;
            client.Timeout = TimeSpan.FromSeconds(20);
        }

        // ########################################################################################

        private async Task<bool> TestConnection()
        {

            if (client == null) { NewConnection(); }
            try
            {
                var response = await client!.GetAsync(urlEndpointEmployee);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                AppLog.Error(ex);
                return false;
            }

        }

        private async Task<VivendiApiState> Login(MainServiceConfiguration.Credential loginCredential)
        {

            NewConnection();
            try
            {

                // JWT empfangen
                var postData = new StringContent(string.Format("{{\"Username\":\"{0}\",\"Password\":\"{1}\"}}",
                                                               loginCredential.Username,
                                                               loginCredential.Passhash),
                                                 Encoding.UTF8, "application/json");
                var response = await client!.PostAsync(urlEndpointLogin, postData);
                if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    // Tritt auf, wenn Nutzername oder Passwort leer
                    return VivendiApiState.CREDENTIALS_ERROR;
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    // Benutzername / Passwort falsch
                    return VivendiApiState.CREDENTIALS_ERROR;
                }
                else if (response.IsSuccessStatusCode)
                {

                    // Nur weitermachen, wenn erfolgreiche Abfrage > JWT empfangen
                    var content = await response.Content.ReadAsStringAsync();
                    var jwtJson = JsonConvert.DeserializeObject<HttpClientJson.ObjLoginJwt>(content);
                    if (jwtJson == null || jwtJson.HasFailed)
                    {
                        // Konnte Json nicht auswerten > App-Fehler
                        AppLog.Error("VivendiApi/Login/jwtJson-Deserialize", (jwtJson != null && jwtJson.Message != null ? jwtJson.Message : string.Empty));
                        return VivendiApiState.SERVER_APP_ERROR;
                    }

                    // JWT dekodieren
                    var jwtHandler = new JwtSecurityTokenHandler();
                    var jwtDecoded = (JwtSecurityToken)jwtHandler.ReadToken(jwtJson.Token!);

                    var claimXsrf = (from x in jwtDecoded.Claims where x.Type == "XsrfToken" select x).First();

                    // Authentifikation speichern
                    client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", claimXsrf.Value);
                    client.DefaultRequestHeaders.Add("Cookie", string.Format("Auth-Token-SelfService={0};", jwtJson.Token!));

                    return VivendiApiState.SUCCESSFUL;

                }

                return VivendiApiState.SERVER_APP_ERROR;

            }
            catch (Exception ex)
            {
                AppLog.Error(ex);
                return ex switch
                {
                    HttpRequestException or TaskCanceledException or SocketException => VivendiApiState.CONNECTION_ERROR,
                    _ => VivendiApiState.SERVER_APP_ERROR,
                };
            }

        }

        // ########################################################################################

        private class HttpClientJson
        {

            public abstract class ObjBase
            {
                public abstract bool HasFailed { get; }
                public abstract bool HasAuthExpired { get; }
            }

            public class ObjLoginJwt : ObjBase
            {

                public string? Message;
                public string? Token;

                [JsonIgnore]
                public override bool HasFailed => Token == null;

                [JsonIgnore]
                public override bool HasAuthExpired => false;

                public ObjLoginJwt() { Message = null; Token = null; }


            }

            public class ObjFetchPublic : ObjBase
            {

                public string? Message;

                // ################################################################################

                public IList<JsonMitarbeiter>? Mitarbeiter;

                // ################################################################################

                public class JsonMitarbeiter
                {

                    public long Id;
                    public string? Name;
                    public string? Vorname;
                    public DateTime FestgelegtBis;
                    public DateTime AbgeschlossenBis;

                    public IList<JsonDatenJeTag>? DatenJeTag;

                    // ################################################################################

                    public JsonMitarbeiter() { Id = 0; Name = "#"; Vorname = "#"; FestgelegtBis = DateTime.MinValue; AbgeschlossenBis = DateTime.MinValue; DatenJeTag = new List<JsonDatenJeTag> { }; }

                }

                public class JsonDatenJeTag
                {

                    public DateTime Date;
                    public IList<JsonDpDienste>? DpDienste;

                    // ################################################################################

                    public JsonDatenJeTag() { Date = DateTime.MinValue; DpDienste = new List<JsonDpDienste> { }; }

                }

                public class JsonDpDienste
                {

                    public DateTime GeaendertAm;
                    public bool IstBestaetigt;

                    public IList<JsonDpDiensteZeiten>? Zeiten;
                    public JsonDienst? Dienst;
                    public JsonBereich? Bereich;

                    // ################################################################################

                    public JsonDpDienste() { GeaendertAm = DateTime.MinValue; IstBestaetigt = true; Zeiten = new List<JsonDpDiensteZeiten> { }; Dienst = new(); Bereich = new(); }

                }

                public class JsonDpDiensteZeiten
                {

                    public DateTime Start;
                    public DateTime End;
                    public double Pause;

                    // ################################################################################

                    public JsonDpDiensteZeiten() { Start = DateTime.MinValue; End = DateTime.MinValue; Pause = 0; }

                }

                public class JsonDienst
                {

                    public long Id;
                    public string? Name;
                    public string? ShortName;
                    public string? Comment;
                    public bool IsAbwesenheitsdienst;
                    public bool IsIsterfassungsdienst;

                    // ################################################################################

                    public JsonDienst() { Id = 0; Name = "#"; ShortName = "#"; Comment = "#"; IsAbwesenheitsdienst = false; IsIsterfassungsdienst = false; }

                }

                public class JsonBereich
                {

                    public long Id;
                    public long ParentId;
                    public string? Name;
                    public string? Kuerzel;

                    // ################################################################################

                    public JsonBereich() { Id = 0; ParentId = 0; Name = "#"; Kuerzel = "#"; }

                }

                // ################################################################################

                [JsonIgnore]
                public override bool HasFailed
                {
                    get
                    {

                        return
                            HasAuthExpired ||
                            Mitarbeiter == null;

                    }
                }

                [JsonIgnore]
                public override bool HasAuthExpired => Message != null && Message.Contains("Token ist abgelaufen oder");

                // ################################################################################

                public ObjFetchPublic() { Message = null; Mitarbeiter = new List<JsonMitarbeiter> { }; }

            }

            public class ObjFetchPrivate : ObjBase
            {

                public string? Message;

                // ################################################################################

                public IList<JsonDay>? Items;

                // ################################################################################

                public class JsonDay
                {

                    public DateTime Day;
                    public IList<JsonDpDienste>? IstDienste;

                    // ################################################################################

                    public JsonDay() { Day = DateTime.MinValue; IstDienste = new List<JsonDpDienste> { }; }

                }

                public class JsonDpDienste
                {

                    public DateTime GeaendertAm;
                    public bool IstBestaetigt;

                    public IList<JsonDpDiensteZeiten>? Zeiten;
                    public JsonDienst? Dienst;
                    public JsonBereich? Bereich;

                    // ################################################################################

                    public JsonDpDienste() { GeaendertAm = DateTime.MinValue; IstBestaetigt = false; Zeiten = new List<JsonDpDiensteZeiten> { }; Dienst = new(); Bereich = new(); }

                }

                public class JsonDpDiensteZeiten
                {

                    public DateTime Start;
                    public DateTime End;
                    public double Pause;

                    // ################################################################################

                    public JsonDpDiensteZeiten() { Start = DateTime.MinValue; End = DateTime.MinValue; Pause = 0; }

                }

                public class JsonDienst
                {

                    public long Id;
                    public string? Name;
                    public string? ShortName;
                    public string? Comment;
                    public bool IsAbwesenheitsdienst;
                    public bool IsIsterfassungsdienst;

                    // ################################################################################

                    public JsonDienst() { Id = 0; Name = "#"; ShortName = "#"; Comment = "#"; IsAbwesenheitsdienst = false; IsIsterfassungsdienst = false; }

                }

                public class JsonBereich
                {

                    public long Id;
                    public long ParentId;
                    public string? Name;
                    public string? Kuerzel;

                    // ################################################################################

                    public JsonBereich() { Id = 0; ParentId = 0; Name = "#"; Kuerzel = "#"; }

                }

                // ################################################################################

                [JsonIgnore]
                public override bool HasFailed
                {
                    get
                    {

                        return
                            HasAuthExpired ||
                            Items == null;

                    }
                }

                [JsonIgnore]
                public override bool HasAuthExpired => Message != null && Message.Contains("Token ist abgelaufen oder");

                // ################################################################################

                public ObjFetchPrivate() { Message = null; Items = new List<JsonDay> { }; }

            }

            public class ObjMasterData : ObjBase
            {

                public string? Message;

                public long Id;

                public string? Vorname;
                public string? Nachname;
                public string? Personalnummer;

                [JsonIgnore]
                public override bool HasFailed => Message != null || Vorname == null || Nachname == null || Personalnummer == null;

                [JsonIgnore]
                public override bool HasAuthExpired => Message != null && Message.Contains("Token ist abgelaufen oder");

                // ################################################################################

                public ObjMasterData() { Id = 0; Message = null; Vorname = "#"; Nachname = "#"; Personalnummer = "#"; }

            }

        }

        // ########################################################################################

        public async Task<VivendiApiState> TestLogin(string username = "", string passhash = "")
        {

            bool isTesting = false;

            // Logins erstellen
            List<MainServiceConfiguration.Credential> toTest = new();
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(passhash))
            {

                // Vorhandene Daten verwenden
                toTest = CredentialBlock.Credentials;

                // Wenn keine Daten vorhanden > Anmeldefehler
                if (!toTest.Any()) { return VivendiApiState.CREDENTIALS_ERROR; }

            }
            else
            {

                isTesting = true;

                // Neue Datei verwenden
                toTest.Add(new(username, passhash));

            }

            // Liste durchlaufen
            foreach (MainServiceConfiguration.Credential credential in toTest)
            {

                var response = await Login(credential);

                if (response == VivendiApiState.SUCCESSFUL) { break; }
                else if (response == VivendiApiState.CREDENTIALS_ERROR) { CredentialBlock.RemoveCredential(credential.Username); }
                else if (response == VivendiApiState.CONNECTION_ERROR) { return VivendiApiState.CONNECTION_ERROR; }
                else { return VivendiApiState.SERVER_APP_ERROR; }

                await Task.Delay(TimeSpan.FromSeconds(0.5));

            }

            // Verbindung testen
            if (await TestConnection())
            {

                if (isTesting) 
                {
                    CredentialBlock.AddCredentials(username, passhash);
                }

                return VivendiApiState.SUCCESSFUL;
            }
            else
            {
                return VivendiApiState.CREDENTIALS_ERROR;
            }

        }

        public async Task<VivendiApiFetchResponse> FetchPublicFromTo(DateTime dateFrom, DateTime dateTo)
        {

            if (client == null) { NewConnection(); }
            string getUrl = "von/" + MainServiceHelper.ConvertDateOnly(dateFrom) +
                            "/bis/" + MainServiceHelper.ConvertDateOnly(dateTo);

            try
            {

                // Verbindung überprüfen
                if (!await TestConnection())
                {

                    // Fehler aufgetreten, Login erneuern
                    var loginResponse = await TestLogin();
                    if (loginResponse != VivendiApiState.SUCCESSFUL) { return new VivendiApiFetchResponse(loginResponse); }

                }

                // Abrufen
                var response = await client!.GetAsync(urlEndpointShiftPublic + getUrl);
                if (response.IsSuccessStatusCode)
                {

                    // Json dekodieren
                    var content = await response.Content.ReadAsStringAsync();
                    var fetchJson = JsonConvert.DeserializeObject<HttpClientJson.ObjFetchPublic>(content);
                    if (fetchJson != null && !fetchJson.HasFailed)
                    {

                        // Json in Listen verschieben
                        Dictionary<string, MainServiceDatabase.Shift> shifts = new();
                        List<MainServiceDatabase.Employee> employee = new();

                        // 1. Mitarbeiter durchlaufen
                        foreach (var jM in fetchJson.Mitarbeiter!)
                        {

                            // Abbrechen, wenn keine Daten
                            if (jM.DatenJeTag == null ||
                                jM.Vorname == null ||
                                jM.Name == null) { continue; }

                            MainServiceDatabase.Employee thisEmployee = new(jM.Id, jM.Vorname, jM.Name);
                            int addCount = 0;

                            // 2. Dienste durchlaufen
                            foreach (var jD in jM.DatenJeTag)
                            {

                                // Abbrechen, wenn keine Daten
                                if (jD.DpDienste == null ||
                                    jD.DpDienste.Count == 0) { continue; }

                                // Dienst wählen, der zuletzt aktualisiert wurde; wenn dort keine Daten abbrechen
                                var current = (from x in jD.DpDienste orderby x.GeaendertAm descending select x).First();
                                if (current.Bereich == null ||
                                    current.Zeiten == null ||
                                    current.Zeiten.Count == 0 ||
                                    current.Dienst == null ||
                                    current.Dienst.Name == null ||
                                    current.Dienst.ShortName == null) { continue; }

                                // Dienst nur berücksichtigen, wenn richtiger Bereich
                                if (current.Bereich.Id != constFilterBereichId) { continue; }

                                // Dienst filtern ( "/" )
                                if (current.Dienst.ShortName == "/") { continue; }

                                // Weitere Zeiten nur Überstunden, deshalb nur erste Zeit auswählen
                                var currentZeit = current.Zeiten.First();

                                MainServiceDatabase.Shift thisShift = new(current.Dienst.Id, current.Dienst.Name, current.Dienst.ShortName, jD.Date, currentZeit.Start, currentZeit.End, TimeSpan.FromMinutes(currentZeit.Pause), jM.Id);
                                if (shifts.ContainsKey(thisShift.PrimaryKey)) { shifts[thisShift.PrimaryKey].BoundEmployee.Add(thisEmployee.VivendiId); }
                                else { shifts.Add(thisShift.PrimaryKey, thisShift); }

                                addCount++;

                            }
                            if (addCount > 0) { employee.Add(thisEmployee); }

                        }

                        // Erfolgreich zurückgeben
                        return new VivendiApiFetchResponse(shifts.Values.ToList<MainServiceDatabase.Shift>(), employee, dateFrom, dateTo);

                    }

                }

                return new VivendiApiFetchResponse(VivendiApiState.SERVER_APP_ERROR);

            }
            catch (Exception ex)
            {
                AppLog.Error(ex);
                return ex switch
                {
                    HttpRequestException or TaskCanceledException => new VivendiApiFetchResponse(VivendiApiState.CONNECTION_ERROR),
                    _ => new VivendiApiFetchResponse(VivendiApiState.SERVER_APP_ERROR),
                };
            }

        }

        public async Task<VivendiApiFetchResponse> FetchPrivateFromTo(DateTime dateFrom, DateTime dateTo, long privateEmployeeId)
        {

            if (client == null) { NewConnection(); }
            string getUrl = "von/" + MainServiceHelper.ConvertDateOnly(dateFrom) +
                            "/bis/" + MainServiceHelper.ConvertDateOnly(dateTo);

            try
            {

                // Verbindung überprüfen
                if (!await TestConnection())
                {

                    // Fehler aufgetreten, Login erneuern
                    var loginResponse = await TestLogin();
                    if (loginResponse != VivendiApiState.SUCCESSFUL) { return new VivendiApiFetchResponse(loginResponse); }

                }

                // Abrufen
                var response = await client!.GetAsync(urlEndpointShiftPrivate + getUrl);
                if (response.IsSuccessStatusCode)
                {

                    // Json dekodieren
                    var content = await response.Content.ReadAsStringAsync();
                    var fetchJson = JsonConvert.DeserializeObject<HttpClientJson.ObjFetchPrivate>(content);
                    if (fetchJson != null && !fetchJson.HasFailed)
                    {

                        // Json in Listen verschieben
                        Dictionary<string, MainServiceDatabase.Shift> shifts = new();

                        // Tage durchlaufen
                        foreach (var jD in fetchJson.Items!)
                        {

                            // Abbrechen, wenn keine Daten
                            if (jD.IstDienste == null || jD.IstDienste.Count == 0) { continue; }

                            // Dienst wählen, der zuletzt aktualisiert wurde; wenn dort keine Daten abbrechen
                            var current = (from x in jD.IstDienste orderby x.GeaendertAm descending select x).First();
                            if (current.Bereich == null ||
                                current.Zeiten == null ||
                                current.Zeiten.Count == 0 ||
                                current.Dienst == null ||
                                current.Dienst.Name == null ||
                                current.Dienst.ShortName == null) { continue; }

                            // Dienst nur berücksichtigen, wenn richtiger Bereich
                            if (current.Bereich.Id != constFilterBereichId) { continue; }

                            // Weitere Zeiten nur Überstunden, deshalb nur erste Zeit auswählen
                            var currentZeit = current.Zeiten.First();

                            // Schicht hinzufügen
                            MainServiceDatabase.Shift thisShift = new(current.Dienst.Id, current.Dienst.Name, current.Dienst.ShortName, jD.Day, currentZeit.Start, currentZeit.End, TimeSpan.FromMinutes(currentZeit.Pause), privateEmployeeId);
                            if (shifts.ContainsKey(thisShift.PrimaryKey)) { shifts[thisShift.PrimaryKey].BoundEmployee.Add(privateEmployeeId); }
                            else { shifts.Add(thisShift.PrimaryKey, thisShift); }

                        }

                        // Erfolgreich zurückgeben
                        return new VivendiApiFetchResponse(shifts.Values.ToList(), new(), dateFrom, dateTo);

                    }

                }

                return new VivendiApiFetchResponse(VivendiApiState.SERVER_APP_ERROR);

            }
            catch (Exception ex)
            {
                AppLog.Error(ex);
                return ex switch
                {
                    HttpRequestException or TaskCanceledException => new VivendiApiFetchResponse(VivendiApiState.CONNECTION_ERROR),
                    _ => new VivendiApiFetchResponse(VivendiApiState.SERVER_APP_ERROR),
                };
            }


        }

        public async Task<VivendiApiMasterDataResponse> FetchPrivateMasterData()
        {

            if (client == null) { NewConnection(); }
            try
            {

                // Verbindung überprüfen
                if (!await TestConnection())
                {

                    // Fehler aufgetreten, Login erneuern
                    var loginResponse = await TestLogin();
                    if (loginResponse != VivendiApiState.SUCCESSFUL) { return new VivendiApiMasterDataResponse(loginResponse); }

                }

                // Abrufen
                var response = await client!.GetAsync(urlEndpointEmployee);
                if (response.IsSuccessStatusCode)
                {

                    // Json dekodieren
                    var content = await response.Content.ReadAsStringAsync();
                    var masterJson = JsonConvert.DeserializeObject<HttpClientJson.ObjMasterData>(content);
                    if (masterJson != null && !masterJson.HasFailed)
                    {

                        // Erfolgreich zurückgeben
                        return new VivendiApiMasterDataResponse(masterJson.Id, masterJson.Nachname!, masterJson.Vorname!, masterJson.Personalnummer!);

                    }

                }

                return new VivendiApiMasterDataResponse(VivendiApiState.SERVER_APP_ERROR);

            }
            catch (Exception ex)
            {
                AppLog.Error(ex);
                return ex switch
                {
                    HttpRequestException or TaskCanceledException => new VivendiApiMasterDataResponse(VivendiApiState.CONNECTION_ERROR),
                    _ => new VivendiApiMasterDataResponse(VivendiApiState.SERVER_APP_ERROR),
                };
            }

        }

    }

    internal enum VivendiApiState
    {
        SUCCESSFUL,
        SERVER_APP_ERROR,
        CREDENTIALS_ERROR,
        CONNECTION_ERROR
    }

    internal class VivendiApiFetchResponse
    {

        public bool IsFailed => FetchState != VivendiApiState.SUCCESSFUL;
        public VivendiApiState FetchState { get; }

        // ########################################################################################

        public List<MainServiceDatabase.Shift> Shifts;
        public List<MainServiceDatabase.Employee> Employee;

        public DateTime FetchedFrom;
        public DateTime FetchedTo;

        // ########################################################################################

        public VivendiApiFetchResponse(VivendiApiState errorType)
        {
            FetchState = errorType; // Nur für Fehler, deshalb ein SUCCESSFUL hier abfangen
            if (errorType == VivendiApiState.SUCCESSFUL) { FetchState = VivendiApiState.SERVER_APP_ERROR; }

            Shifts = new();
            Employee = new();
            FetchedFrom = DateTime.MinValue;
            FetchedTo = DateTime.MinValue;
        }

        public VivendiApiFetchResponse(List<MainServiceDatabase.Shift> shifts,
                                       List<MainServiceDatabase.Employee> employee,
                                       DateTime fetchedFrom, DateTime fetchedTo)
        {
            FetchState = VivendiApiState.SUCCESSFUL;
            Shifts = shifts;
            Employee = employee;
            FetchedFrom = fetchedFrom;
            FetchedTo = fetchedTo;
        }

    }
    internal class VivendiApiMasterDataResponse
    {

        public bool IsFailed => FetchState != VivendiApiState.SUCCESSFUL;
        public VivendiApiState FetchState { get; }

        // ########################################################################################

        public long EmployeeVivendiId { get; }
        public string LastName { get; }
        public string FirstName { get; }
        public string EmployeeNumber { get; }

        // ########################################################################################

        public VivendiApiMasterDataResponse(VivendiApiState errorType)
        {
            FetchState = errorType; // Nur für Fehler, deshalb ein SUCCESSFUL hier abfangen
            if (errorType == VivendiApiState.SUCCESSFUL) { FetchState = VivendiApiState.SERVER_APP_ERROR; }

            EmployeeVivendiId = -1;
            LastName = "";
            FirstName = "";
            EmployeeNumber = "";
        }

        public VivendiApiMasterDataResponse(long employeeId, string lastname, string firstname, string persNumber)
        {
            FetchState = VivendiApiState.SUCCESSFUL;
            EmployeeVivendiId = employeeId;
            LastName = lastname;
            FirstName = firstname;
            EmployeeNumber = persNumber;
        }

    }


    #endregion

}

#region backup

//private void RunBackgroundFetch()
//{

//    // Abbrechen, sollte der Thread noch laufen
//    if (backgroundThread != null && backgroundThread.IsAlive) { return; }

//    // Variablen initialisieren
//    DateTime scheduledClean = DateTime.Now;

//    // Thread erstellen
//    backgroundThread = new(async () =>
//    {

//        // Damit wenigstens der erste Tag schneller lädt, kurz den Monatsabruf verzögern
//        //Thread.Sleep(TimeSpan.FromMinutes(1));

//        // Schleife dauerhat wiederholen
//        while (true)
//        {

//            try
//            {

//                // Verbinden
//                var response = await api.FetchPublicFromTo(DateTime.Now, DateTime.Now.AddDays(31));
//                if (response.IsFailed)
//                {

//                    // Wenn die Anmeldedaten nicht stimmen, kann dieser BackgroundFetcher beendet werden
//                    // bis jemand das Programm öffnet und sich anmeldet. TODO: StateObjekt aktualisieren & ignorieren
//                    //if (response.FetchState == VivendiApiState.CONNECTION_ERROR) { break; }

//                    // Alle anderen Fehler (Internet fehlt, Serverfehler) werden ignoriert und in 2.5h erneut probiert

//                }
//                else
//                {

//                    // Wenn erfolgreich abgerufen & Bekannte Schichten aktualisieren
//                    db.ImportFetchPublic(response);
//                    response.Shifts.ForEach(x => conf.AddKnownShift(x));
//                    conf.SaveInstance();

//                }

//                // Datenbank bereinigen
//                if (scheduledClean < DateTime.Now)
//                {
//                    db.CleanPublicCache();
//                    scheduledClean = DateTime.Now.AddDays(3);
//                }

//            }
//            catch (Exception ex)
//            {
//                AppLog.Error(ex);
//            }

//            // Abwarten
//            Thread.Sleep(TimeSpan.FromHours(2.5));

//        }

//    })
//    { IsBackground = true };

//    // Thread starten
//    backgroundThread.Start();

//}

#endregion