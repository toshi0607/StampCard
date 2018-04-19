using Syncfusion.SfCalendar.XForms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;
using Microsoft.WindowsAzure.MobileServices;
using System.Net.Http;
using System.Windows.Input;
using System.Diagnostics;

namespace StampCard
{
    public partial class MainPage : ContentPage
    {
        private CalendarEventCollection calendarEventCollection = new CalendarEventCollection();
        CalendarDateManager cDateManager;
        private static HttpClient client = new HttpClient();

        public MainPage()
        {
            InitializeComponent();
            cDateManager = CalendarDateManager.DefaultManager;

            calendar.Locale = new System.Globalization.CultureInfo("ja-JP");
            calendar.DataSource = calendarEventCollection;
        }

        protected async override void OnAppearing()
        {
            base.OnAppearing();
            await RefreshCarendarAsync(DateTime.Today);
        }

        async Task RefreshCarendarAsync(DateTime dt)
        {
            IsRequesting(true);

            CalendarEventCollection newCalendarEventCollection = new CalendarEventCollection();

            var calendarDates = await cDateManager.GetCalendarDatesAsync(dt);
            foreach (var cd in calendarDates)
            {
                newCalendarEventCollection.Add(
                    new CalendarInlineEvent()
                    {
                        StartTime = cd.StampAt,
                        EndTime = cd.StampAt,
                        Color = ColorByTypeColor(cd.Type),
                    });
            }

            // 形だけでも待たないと落ちる
            await Task.Delay(10);

            calendarEventCollection = newCalendarEventCollection;
            calendar.DataSource = newCalendarEventCollection;

            IsRequesting(false);
        }

        void IsRequesting(bool flag)
        {
            lastMonthButton.IsEnabled = !flag;
            refreshButton.IsEnabled = !flag;
            nextMonthButton.IsEnabled = !flag;
            indicator.IsRunning = flag;
        }

        Color ColorByTypeColor(CalendarDate.Status type)
        {
            switch (type)
            {
                case CalendarDate.Status.Reviewing:
                    return Color.Yellow;
                case CalendarDate.Status.Approved:
                    return Color.Green;
                case CalendarDate.Status.Rejected:
                    return Color.Red;
                default:
                    throw new Exception("Unexpected type");
            }
         }
        void UpdateEvents(DateTime calendarDate)
        {
            DateTime dt = new DateTime(calendarDate.Year, calendarDate.Month, calendarDate.Day);
            var ev = calendarEventCollection.Where(e => e.StartTime == dt && e.EndTime == dt);
            if (ev.Count() == 0)
            {
                calendarEventCollection.Add(
                    new CalendarInlineEvent()
                    {
                        StartTime = dt,
                        EndTime = dt,
                        Color = Color.Yellow
                    });
                calendar.DataSource = calendarEventCollection;
            }
            else
            {
                throw new Exception($"{calendarDate} is invalid");
            }
        }

        async void Handle_OnCalendarTapped(object snder, EventArgs e)
        {
            var ev = ((CalendarTappedEventArgs)e);
            var cev = calendarEventCollection.Where(ce => ce.StartTime.Year == ev.datetime.Year &&
                ce.StartTime.Month == ev.datetime.Month && ce.StartTime.Day == ev.datetime.Day).SingleOrDefault();
            if (cev?.Color != null)
                return;

            var result = await DisplayAlert("参加スタンプをリクエストしますか？", "", "はい", "いいえ");
            if (result)
            {
                var cDate = new CalendarDate()
                {
                    StampAt = ev.datetime,
                    Type = CalendarDate.Status.Reviewing,
                };
                try
                {
                    IsRequesting(true);
                    await cDateManager.SaveCalendarDateAsync(cDate);
                    await SendApprovalRequest(cDate);
                    IsRequesting(false);
                }
                catch (Exception ex)
                {
                    Debug.Write(ex);
                }
                
                UpdateEvents(((CalendarTappedEventArgs)e).datetime);
            }
        }

        async Task<string> SendApprovalRequest(CalendarDate cDate)
        {
            var payload = $"{{\"calendarDate\":\"{cDate.StampAt.ToString()}\"}}";
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(Variables.ApprovalRequestURL, content);
            return await response.Content.ReadAsStringAsync();
        }

        async void OnRefreshButtonClicked(object snder, EventArgs e)
        {
            await RefreshCarendarAsync(calendar.DisplayDate);
        }

        void OnNextMonthButtonClicked(object snder, EventArgs e)
        {
            DependencyService.Get<ICalendarDirection>().Forward();
        }

        void OnLastMonthButtonClicked(object snder, EventArgs e)
        {
            DependencyService.Get<ICalendarDirection>().Backward();
        }

        async void Handle_MonthChanged(object snder, EventArgs e)
        {
            await RefreshCarendarAsync(calendar.DisplayDate);
        }
    }
}
