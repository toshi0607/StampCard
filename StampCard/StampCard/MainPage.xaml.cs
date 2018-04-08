using Syncfusion.SfCalendar.XForms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;
using Microsoft.WindowsAzure.MobileServices;

namespace StampCard
{
    public partial class MainPage : ContentPage
    {
        private CalendarEventCollection calendarEventCollection = new CalendarEventCollection();
        CalendarDateManager manager;

        public MainPage()
        {
            InitializeComponent();
            calendar.Locale = new System.Globalization.CultureInfo("ja-JP");

            this.calendar.DataSource = calendarEventCollection;
            //this.UpdateEvents(DateTime.Today);
        }

        protected async override void OnAppearing()
        {
            base.OnAppearing();
            CalendarEventCollection newCalendarEventCollection = new CalendarEventCollection();

            manager = CalendarDateManager.DefaultManager;
            var calendarDates = await manager.GetCalendarDatesAsync(DateTime.Today);
            foreach (var cd in calendarDates)
            {
                newCalendarEventCollection.Add(
                    new CalendarInlineEvent()
                    {
                        StartTime = cd.StampAt,
                        EndTime = cd.StampAt,
                        Color = colorByTypeColor(cd.Type),
                    });
            }

            // 形だけでも待たないと落ちる
            await Task.Delay(10);
            // こう書きたいけどUI更新されない
            calendarEventCollection = newCalendarEventCollection;
            this.calendar.DataSource = calendarEventCollection;
            //this.calendar.DataSource = newCalendarEventCollection;
        }

        Color colorByTypeColor(CalendarDate.Status type)
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
                this.calendar.DataSource = calendarEventCollection;
            }
            else
            {
                throw new Exception($"{calendarDate} is invalid");
            }
        }

        public async void Handle_OnCalendarTapped(object snder, EventArgs e)
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
                await this.manager.SaveCalendarDateAsync(cDate);
                UpdateEvents(((CalendarTappedEventArgs)e).datetime);
            }
        }
    }
}
