using Syncfusion.SfCalendar.XForms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace StampCard
{
	public partial class MainPage : ContentPage
	{
        private CalendarEventCollection calendarEventCollection = new CalendarEventCollection();

        public MainPage()
		{
			InitializeComponent();
            this.UpdateEvents(DateTime.Today);
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
            } else if (ev.Count() == 1)
            {
                this.calendar.DataSource.Remove(ev.Single());
                calendarEventCollection.Add(
                   new CalendarInlineEvent()
                   {
                       StartTime = dt,
                       EndTime = dt,
                       Color = Color.Green
                   });
                this.calendar.DataSource = calendarEventCollection;
            }
        }

        void Handle_OnCalendarTapped(object snder, EventArgs e)
        {
            UpdateEvents(((CalendarTappedEventArgs)e).datetime);
        }
    }
}
