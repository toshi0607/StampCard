using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.MobileServices;

namespace StampCard
{
    class CalendarDateManager
    {
        static CalendarDateManager defaultInstance = new CalendarDateManager();
        MobileServiceClient client;
        IMobileServiceTable<CalendarDate> calendarDateTable;
        private CalendarDateManager()
        {
            this.client = new MobileServiceClient(Variables.StampCardURL);

            this.calendarDateTable = client.GetTable<CalendarDate>();
        }

        public static CalendarDateManager DefaultManager
        {
            get
            {
                return defaultInstance;
            }
            private set
            {
                defaultInstance = value;
            }
        }

        public MobileServiceClient CurrentClient
        {
            get { return client; }
        }

        public bool IsOfflineEnabled
        {
            get { return false; }
        }

        public async Task<ObservableCollection<CalendarDate>> GetCalendarDatesAsync(DateTime dt)
        {
            try
            {
                IEnumerable<CalendarDate> cDates = await calendarDateTable.ToEnumerableAsync();

                // .Where(cd => firstDayOfMonth.ToString("yyyy-mm-dd") <= cd.StampAt.ToString("yyyy-mm-dd") && cd.StampAt <= dt)的な書き方もできず...
                cDates = cDates.Where(cd => cd.StampAt.Year == dt.Year && cd.StampAt.Month == dt.Month);

                return new ObservableCollection<CalendarDate>(cDates);
            }
            catch (MobileServiceInvalidOperationException msioe)
            {
                Debug.WriteLine(@"Invalid sync operation: {0}", msioe.Message);
            }
            catch (Exception e)
            {
                Debug.WriteLine(@"Sync error: {0}", e.Message);
            }
            return null;
        }

        public async Task SaveCalendarDateAsync(CalendarDate cDate)
        {
            if (cDate.Id == null)
            {
                await calendarDateTable.InsertAsync(cDate);
            }
            else
            {
                await calendarDateTable.UpdateAsync(cDate);
            }
        }
    }
}
