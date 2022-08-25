# Scraper for Badminton Courts

My friends and I like playing badminton on the weekends. Unfortunately, so do a lot of other people and the courts get booked quickly. Hence I built this.

It looks for courts on the site and compiles a list of free time slots available for booking. Unfortunately booking is not automated because I don't dare to put any money on my shitty code.

This app's dockerized so you don't have to handle dependency nonsense.

## Quick Start

### Run

Have docker available

Setup `BADMINTON_URL` environment variable to be the booking URL. Leaving it out here to prevent people from spamming it.

```bash
docker pull ghcr.io/linanqiu/badminton-scraper:latest
docker run -e BADMINTON_URL='http://actualurl' ghcr.io/linanqiu/badminton-scraper dotnet BadmintonScraperConsole.dll -r -m 150 -s "2022-08-27 07:00" -e "2022-08-27 20:00" -c 7
```

Here are the command line arguments available:

* `-m`: Minimum duration (in minutes) of booking e.g. `240` for 4 hours. Default is 0 (i.e. no min diration).
* `-s`: Starting datetime in yyyy-mm-dd hh:mm format e.g. `2022-08-27 18:00`. Defaults to now.
* `-e`: Ending datetime in yyyy-mm-dd hh:mm format e.g. `2022-08-27 22:00`. Defaults to now + 3 days.
* `-c`: Courts to exclude separated by comma e.g. `7,1`. Defaults to none. This is useful for excluding the training court (Court 7)
* `-r`: Keep looping the search every 5 min until search has at least one result. Default to `false`.

In addition, the scraper can optionally send an email if it finds at least one valid time slot. **NOTE: this email only goes out if the scraper finds at least one match. No email is sent if no results are round.** This is useful when the scraper is set on repeat so it keeps searching until something is found and a notification is sent.

* `BADMINTON_SENDGRID_APIKEY`: SendGrid API key to enable automated email sending
* `BADMINTON_FROM_EMAIL`: Email address to send email from (e.g. `lin.dan@badminton.com`)
* `BADMINTON_TO_EMAILS`: Comma delimited email addresses to send result email to (e.g. `lee.chongwei@badminton.com,lin.dan@badminton.com`).

Running the container with env vars set:

```bash
docker pull ghcr.io/linanqiu/badminton-scraper:latest
docker run \
	-e BADMINTON_URL='http://actualurl' \
	-e BADMINTON_SENDGRID_APIKEY='asdfasdfasdf' \
	-e BADMINTON_FROM_EMAIL='lin.dan@badminton.com' \
	-e BADMINTON_TO_EMAILS='lee.chongwei@badminton.com,lin.dan@badminton.com' \
	ghcr.io/linanqiu/badminton-scraper \
	dotnet BadmintonScraperConsole.dll -r -m 150 -s "2022-08-27 07:00" -e "2022-08-27 20:00" -c 7
```

### Build

We start with the dotnetcore SDK 3.1 image. I haven't updated my visual studio in a while so I'm not on .NET 6 yet. Don't blame me.

```bash
git clone https://github.com/linanqiu/badminton-scraper.git
cd badminton-scraper
docker build -t ghcr.io/linanqiu/badminton-scraper .
docker run ghcr.io/linanqiu/badminton-scraper dotnet BadmintonScraperConsole.dll -r -m 150 -s "2022-08-27 07:00" -e "2022-08-27 20:00" -c 7
```