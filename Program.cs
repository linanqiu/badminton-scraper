using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommandLine;
using OpenQA.Selenium;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Support.UI;
using SendGrid;
using SendGrid.Helpers.Mail;
using TimeSpan = System.TimeSpan;

namespace BadmintonScraperConsole
{
    internal class Program
    {
        internal class Options
        {
            [Option('m', "min", Required = false, Default = 0, HelpText = "Minimum duration (in minutes) of booking e.g. `240` for 4 hours. Default is 0 (i.e. no min diration)")]
            public int MinDuration { get; set; }

            [Option('s', "start", Required = false, Default = "", HelpText = "Starting datetime in yyyy-mm-dd hh:mm format e.g. `2022-08-27 18:00`. Defaults to now.")]
            public string StartDateTime { get; set; }

            [Option('e', "end", Required = false, Default = "", HelpText = "Ending datetime in yyyy-mm-dd hh:mm format e.g. `2022-08-27 22:00`. Defaults to now + 3 days.")]
            public string EndDateTime { get; set; }

            [Option('c', "courts", Required = false, Default = new int[0], HelpText = "Courts to exclude separated by comma e.g. `7,1`. Defaults to none", Separator = ',')]
            public IEnumerable<int> CourtsToExclude { get; set; }

            [Option('r', "repeat", Required = false, Default = false, HelpText = "Keep looping the search every 5 min until search has at least one result. Default to false.")]
            public bool Repeat { get; set; }
        }

        public static void Main(string[] args)
        {
            var a = CommandLine.Parser.Default.ParseArguments<Options>(args)
                .WithParsed(Search)
                .WithNotParsed(errors =>
                {
                    foreach (var error in errors)
                    {
                        Console.Error.WriteLine(error);
                    }
                });
        }

        private static void Search(Options opts)
        {
            if (opts.Repeat)
            {
                Console.WriteLine("Repeating search every 5min until a result is found");
                var repeat = true;
                while (repeat)
                {
                    repeat = !SearchOnce(opts);
                    Console.WriteLine("Waiting 5 minutes");
                    Task.Delay(TimeSpan.FromMinutes(5)).Wait();
                }
            }
            else
            {
                SearchOnce(opts);
            }
        }

        private static bool SearchOnce(Options opts)
        {
            var url = Environment.GetEnvironmentVariable("BADMINTON_URL") ?? "";
            if (string.IsNullOrEmpty(url))
            {
                throw new InvalidOperationException("BADMINTON_URL env var is not specified or empty. Need URL for scraping.");
            }

            var minDuration = TimeSpan.FromMinutes(opts.MinDuration);
            var startDateTime = string.IsNullOrEmpty(opts.StartDateTime) ? DateTime.Now : DateTime.Parse(opts.StartDateTime);
            var endDateTime = string.IsNullOrEmpty(opts.EndDateTime) ? DateTime.Now + TimeSpan.FromDays(2) : DateTime.Parse(opts.EndDateTime);
            var courtsToExclude = opts.CourtsToExclude.ToArray();

            Console.WriteLine($"Running search with " +
                              $"minDuration={minDuration} " +
                              $"startDateTime={startDateTime} " +
                              $"endDateTime={endDateTime} " +
                              $"courtsToExclude={string.Join(", ", courtsToExclude)}");

            var options = new EdgeOptions();
            options.AddArguments("--headless");
            options.AddArguments("--no-sandbox");

            using var driver = new EdgeDriver(options);
            var results = new List<(TimeSpan, int, DateTime)>();
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(5);

            driver.Navigate().GoToUrl(url);

            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10))
            {
                PollingInterval = TimeSpan.FromSeconds(1)
            };
            wait.IgnoreExceptionTypes(typeof(NoSuchElementException));

            var appointmentElements = wait.Until(d => d.FindElements(By.CssSelector(".select-item-box")))
                .Select(e => new
                {
                    Element = e.FindElement(By.CssSelector("button")),
                    Duration = e.FindElement(By.CssSelector(".duration")).Text.Trim().Replace("\\s+", " ").ConvertDuration()
                })
                .Where(e => e.Duration >= minDuration)
                .ToArray();

            foreach (var appointmentElement in appointmentElements)
            {
                var appointmentDuration = appointmentElement.Duration;
                Console.WriteLine($"Searching for all eligible courts with duration={appointmentDuration:g}");

                appointmentElement.Element.Click();

                var calendarElements = wait.Until(d => d.FindElements(By.CssSelector(".calendar-select-box")))
                    .Where(e => e.FindElement(By.CssSelector("label")).Text != "Any available")
                    .Select(e => new
                    {
                        Element = e.FindElement(By.CssSelector("button")),
                        Court = e.FindElement(By.CssSelector("label")).Text.Trim().ConvertCourt()
                    })
                    .Where(e => !courtsToExclude.Contains(e.Court))
                    .ToArray();

                foreach (var calendarElement in calendarElements)
                {
                    var court = calendarElement.Court;
                    Console.WriteLine($"Searching for all timeslots with duration={appointmentDuration:g} court={court}");

                    calendarElement.Element.Click();

                    wait.Until(d => d.FindElement(By.CssSelector(".choose-date-time")).Text.Trim() != "LOADING...");

                    var dateTimePage1Element = driver.FindElement(By.CssSelector(".choose-date-time"));
                    var timesPage1 = dateTimePage1Element.FindElements(By.CssSelector(".time-selection"))
                        .Select(e => DateTime.Parse(e.GetAttribute("value")))
                        .ToArray();

                    var calendarNextElement = driver.FindElement(By.CssSelector(".calendar-next"));
                    calendarNextElement.Click();

                    wait.Until(d => d.FindElement(By.CssSelector(".choose-date-time")).Text.Trim() != "LOADING...");
                    var dateTimePage2Element = driver.FindElement(By.CssSelector(".choose-date-time"));
                    var timesPage2 = dateTimePage2Element.FindElements(By.CssSelector(".time-selection"))
                        .Select(e => DateTime.Parse(e.GetAttribute("value")));

                    var timesAll = timesPage1.Union(timesPage2).OrderBy(d => d).ToArray();
                    var times = timesAll.Where(d => d >= startDateTime && d <= endDateTime).ToArray();

                    foreach (var time in times)
                    {
                        var line = $"{appointmentDuration:g},{court},{time:g}";
                        Console.WriteLine(line);
                        results.Add((appointmentDuration, court, time));
                    }

                    Console.WriteLine($"duration={appointmentDuration:g} court={court} eligibleTimeSlots={times.Length} allTimeSlots={timesAll.Length}");
                    var backButtonCalendars = driver.FindElements(By.CssSelector(".back-button")).Where(e => e.Text.ToLower().Trim() == "view all calendars").Single();
                    backButtonCalendars.Click();
                    //calendarElement.Element.Click();

                }

                var backButtonAppointments = driver.FindElements(By.CssSelector(".back-button")).Where(e => e.Text.ToLower().Trim() == "view all appointments").Single();
                backButtonAppointments.Click();
                //appointmentElement.Element.Click();
            }

            driver.Quit();

            var body = new StringBuilder();
            if (results.Any())
            {
                foreach (var (duration, court, time) in results)
                {
                    body.AppendLine($"{duration:g},Court {court},{time:g}");
                    body.AppendLine(Environment.NewLine);
                    body.AppendLine(Environment.NewLine);
                }

                var subject = $"Badminton Scraping Results " +
                              $"MinDuration={minDuration:g} Start={startDateTime:g} End={endDateTime:g} " +
                              $"CourtsExcluded={string.Join(", ", courtsToExclude)}";

                SendEmail(body: body, subject: subject);

                return true;
            }

            body.AppendLine("No available times found");
            return false;
        }

        private static void SendEmail(StringBuilder body, string subject)
        {
            var sendGridApiKey = Environment.GetEnvironmentVariable("BADMINTON_SENDGRID_APIKEY") ?? "";
            var emailFrom = Environment.GetEnvironmentVariable("BADMINTON_FROM_EMAIL") ?? "";
            var emailsTo = (Environment.GetEnvironmentVariable("BADMINTON_TO_EMAILS") ?? "").Split(',');

            if (string.IsNullOrEmpty(sendGridApiKey))
            {
                Console.WriteLine("Skipping sending email. BADMINTON_SENDGRID_APIKEY env var is not set");
                Console.WriteLine(body.ToString());
            }
            else if (!emailFrom.IsValidEmailAddress())
            {
                Console.WriteLine("Skipping sending email. BADMINTON_FROM_EMAIL env var is not set or not a valid email");
                Console.WriteLine(body.ToString());
            }
            else if (!emailsTo.All(e => e.IsValidEmailAddress()))
            {
                Console.WriteLine("Skipping sending email. BADMINTON_TO_EMAILS env var is not set or are not all valid emails");
                Console.WriteLine(body.ToString());
            }
            else
            {
                var client = new SendGridClient(sendGridApiKey);
                var from = new EmailAddress(emailFrom, emailFrom);
                var tos = emailsTo.Select(e => e.Trim()).Select(e => new EmailAddress(e, e)).ToList();
                var content = body.ToString();
                var msg = MailHelper.CreateSingleEmailToMultipleRecipients(from: from, tos: tos, subject: subject, plainTextContent: content, htmlContent: content);
                client.SendEmailAsync(msg).Wait(TimeSpan.FromMinutes(1));
            }
        }
    }

    internal static class Helpers
    {
        private static readonly EmailAddressAttribute EmailAttribute = new EmailAddressAttribute();

        internal static TimeSpan ConvertDuration(this string duration)
        {
            const string pattern = @"(?:([0-9]+) hour(?:s*))?\s*(?:([0-9]+) minute(?:s*))?";
            var match = Regex.Match(duration, pattern);
            var hoursString = match.Groups[1].Value;
            var minutesString = match.Groups[2].Value;
            var hours = string.IsNullOrEmpty(hoursString)
                ? TimeSpan.Zero
                : TimeSpan.FromHours(int.Parse(hoursString));
            var minutes = string.IsNullOrEmpty(minutesString)
                ? TimeSpan.Zero
                : TimeSpan.FromMinutes(int.Parse(minutesString));
            return hours + minutes;
        }

        internal static int ConvertCourt(this string court)
        {
            const string pattern = @"NYBC.*Court\s+([0-9]+).*";
            var match = Regex.Match(court, pattern);
            return int.Parse(match.Groups[1].Value);
        }

        internal static bool IsValidEmailAddress(this string address) => !string.IsNullOrEmpty(address) && EmailAttribute.IsValid(address);
    }
}
