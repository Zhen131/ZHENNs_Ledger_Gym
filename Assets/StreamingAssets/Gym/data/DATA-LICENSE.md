# Data licence

**Data: Binance Vision** (https://data.binance.vision)

This notice applies to every data file in this folder (`Assets/StreamingAssets/Gym/data/`), including `BTCUSDT-1h.csv` and `BTCUSDT-1h.manifest.json`.

| Item | Value |
| --- | --- |
| Source | Binance Vision public data, spot market, monthly 1-hour kline archives (`data/spot/monthly/klines/{SYMBOL}/1h/`) |
| Licence | [Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International (CC BY-NC-SA 4.0)](https://creativecommons.org/licenses/by-nc-sa/4.0/) |
| Terms | [Binance Vision Dataset Terms and Conditions, version 1.0, last updated 2026-08-26](https://github.com/binance/binance-public-data/blob/master/TERMS_AND_CONDITIONS.md) |
| Downloaded | 2026-10-01 (UTC); the exact time is `downloaded_at_utc` in the manifest |

## Changes made to the original data

The files here are a derivative of the Binance Vision archives, produced by `tools/data/fetch_binance_klines.py`:

1. Only the first six columns are kept: open time, open, high, low, close, volume. The other six columns (close time, quote volume, trade count, taker volumes, Ignore) are dropped.
2. Open times are unified to milliseconds since the Unix epoch (archives from 2025-01-01 on use microseconds).
3. Missing hours are filled with a flat candle at the previous candle's close (open = high = low = close = previous close, volume = 0). Only earlier values are used.
4. Candles whose open time is not on the hour are dropped and their hours filled as in step 3. In `BTCUSDT-1h.csv` this affects 43 candles between 2018-02-09 09:28 and 2018-02-11 03:28 UTC, after an exchange outage. The manifest records this under `off_hour_rows`.

Prices and volumes are otherwise copied character for character. Every archive's SHA-256 is listed in the manifest.

These derived files are distributed under the same licence, CC BY-NC-SA 4.0, for non-commercial educational use.

This project is not affiliated with, sponsored or endorsed by Binance.
