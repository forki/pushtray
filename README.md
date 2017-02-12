# Pushtray

Simple phone notifications on Linux via Pushbullet.

### Usage

```
Usage:
  pushtray connect [options]
  pushtray sms <number> <message> [--device=<name>] [options]
  pushtray list devices [options]
  pushtray (-h | --help)"

Options:
  --access-token=<token>      Set the access token. This will override the
                              config file value.
  --encrypt-pass=<pass>       Set the encrypt password. This will override the
                              config file value.
  --no-tray-icon              Don't show a tray icon.
  --no-icon-animations        Only show static tray icons.
  --sms-notify-icon=<icon>    Change the stock icon for SMS notifications.
  --ignore-sms <numbers>      Don't show SMS notifications from these numbers
                              <numbers> is a comma-separated list or a single
                              asterisk to ignore all.
  --notify-format=<fmt>       Set notification format style (full | short)
  --notify-line-wrap=<wrap>   Set the line wrap width of notifications
                              (i.e. the maximum width)
  --notify-line-pad=<pad-to>  Set the minimum line width of notifications
  --icon-style=<style>        Customize the tray icon style (light | dark)
  --log=<log-level>           Enable all logging messages at <log-level>
                              and higher
```

### Build instructions

**Arch Linux**

First install the required dependencies:
``` console
$ sudo pacman -S mono fsharp notify-sharp-3
```

Then you should be able to simply do the following:
``` console
$ git clone https://github.com/jjpatel/pushtray
$ cd pushtray
$ make
```

**Other distros**

1. Follow [this guide](http://fsharp.org/use/linux/) to install Mono and F#.
2. Install `gtk-sharp-3` and `notify-sharp-3`. They should be available from your distro's package manager. If they're not found try some variations (e.g. `notify-sharp3` or `notify-sharp-3.0`).
3. Then just clone the repo and run Make as shown above.

### Similar projects

* [pushbullet-indicator](http://www.atareao.es/tag/pushbullet-indicator/)

### License

GPL3
