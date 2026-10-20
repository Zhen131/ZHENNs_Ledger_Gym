# Data and licence

**Data: Binance Vision.** The environment replays spot 1-hour candles from [Binance Vision](https://data.binance.vision) of five coins against USDT: BTC (the main symbol, from 2017-08-17 04:00 UTC), ETH, ADA, DOGE and XRP, all to 2026-08-31 23:00 UTC. The processed files are committed in `unity/Assets/StreamingAssets/Gym/data/` as `<SYMBOL>-1h.csv`, each next to a manifest with the SHA-256 of every source archive and of the CSV itself:

| Symbol | First candle (UTC) | Last candle (UTC) | Rows | Filled hours | Off-hour candles dropped | `csv_sha256` |
| --- | --- | --- | ---: | ---: | ---: | --- |
| `BTCUSDT` | 2017-08-17 04:00 | 2026-08-31 23:00 | 79,244 | 170 | 43 | `4739c139dc501e38498589359db093394dcee86cabde5a6e37c12d726d084242` |
| `ETHUSDT` | 2017-08-17 04:00 | 2026-08-31 23:00 | 79,244 | 170 | 43 | `4c912dbc7028f544fa28ea4c5cc2d5882ffabfa82df4b4c49d095e0daa26be25` |
| `ADAUSDT` | 2018-04-17 04:00 | 2026-08-31 23:00 | 73,412 | 87 | 0 | `6badda18e80edef0682ca092220adc7b1e76bf963bf1aee7bde7597a88c334e1` |
| `DOGEUSDT` | 2019-07-05 12:00 | 2026-08-31 23:00 | 62,748 | 43 | 0 | `f4030a9eb3f0b2ba6b72d640ec526a2b62cc90811fc2e1eca6a71c2a4fc91453` |
| `XRPUSDT` | 2018-05-04 08:00 | 2026-08-31 23:00 | 73,000 | 87 | 0 | `fb24dd25a08112ac9c870d959ba760f465592763cf03ffdcdcf2b2b74b4b2ca7` |

Training uses the main symbol; the other four are listed in the `symbols` of `gym-config.json` and chosen with `-gymSymbol` (see [evaluation.md](evaluation.md#symbols)). DOGE is marked `evalOnly`: a coin the agent is only ever tested on. Each symbol's order rules (`minNotional`, `stepSize`) are in `symbols.json`; DOGE, BNB and XRP were read from the public `api/v3/exchangeInfo` endpoint on 2026-10-20.

The data is licensed [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/) under the [Binance Vision Dataset Terms v1.0 (2026-08-26)](https://github.com/binance/binance-public-data/blob/master/TERMS_AND_CONDITIONS.md); see [`DATA-LICENSE.md`](../unity/Assets/StreamingAssets/Gym/data/DATA-LICENSE.md) for the attribution and the list of changes. This project is not affiliated with, sponsored or endorsed by Binance. The code has no licence file yet.

To regenerate the data (standard library only; raw archives are cached in `data/raw/`, which Git ignores):

```bash
python scripts/data/fetch_binance_klines.py --self-test
python scripts/data/fetch_binance_klines.py --symbol BTCUSDT   # likewise ETHUSDT, ADAUSDT, DOGEUSDT, XRPUSDT
```

By default the script stops at 2026-08, the end of the test segment (pass `--end YYYY-MM` to add newer months), and drops the 43 candles of February 2018 that start at hh:28 instead of on the hour, forward-filling those hours (`--off-hour error` stops instead, `--off-hour floor` moves them to the hour). So the default command rebuilds exactly the committed file: the `csv_sha256` in the manifest is `4739c139dc501e38498589359db093394dcee86cabde5a6e37c12d726d084242` for BTCUSDT and the one in the table above for the others. A coin listed after 2017-08 needs no `--start`: the script skips the months before its first archive.

When the archives are downloaded afresh (another machine, an empty `data/raw/`), the CSV is the same and the manifest differs only in `downloaded_at_utc`, the time the archives were saved; do not commit that change. The script prints a `!!! WARNING` when the result has off-hour candles or gaps over 24 hours that the committed data does not have (BTCUSDT and ETHUSDT each have 43 off-hour candles and one 75-hour gap from 2018-02-08 01:00; ADAUSDT, DOGEUSDT and XRPUSDT have none), so a new stretch of flat filler from a later `--end` does not go unnoticed.

**Correlation with BTC.** `scripts/data/correlation.py` (standard library only) prints the Pearson correlation of daily and hourly log returns between hourly CSVs, over the window all of them have data for, from the first candle of the coin listed last to 2021-12-31, the end of the training segment (so no validation or test data is used). Daily returns use the close of the 00:00 UTC candle. With the five committed coins and BNB (fetched but not kept), the window is 2019-07-05 12:00 to 2021-12-31 23:00 UTC (DOGE was listed last; 909 daily returns):

| Daily log returns | BTC | ETH | ADA | DOGE | BNB | XRP |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| BTC | 1.0000 | 0.8255 | 0.6869 | 0.4508 | 0.7313 | 0.5930 |
| ETH | 0.8255 | 1.0000 | 0.7702 | 0.4505 | 0.7749 | 0.6669 |
| ADA | 0.6869 | 0.7702 | 1.0000 | 0.4111 | 0.6982 | 0.6236 |
| DOGE | 0.4508 | 0.4505 | 0.4111 | 1.0000 | 0.3622 | 0.3972 |
| BNB | 0.7313 | 0.7749 | 0.6982 | 0.3622 | 1.0000 | 0.6149 |
| XRP | 0.5930 | 0.6669 | 0.6236 | 0.3972 | 0.6149 | 1.0000 |

The fifth coin was chosen between BNB and XRP by this table: the one whose daily returns correlate less with BTC's, XRP if the two were within 0.02. XRP (0.5930) is 0.1383 below BNB (0.7313), so XRP is in and BNB's data is not committed. Hourly returns rank the same way (BTC: ETH 0.8335, BNB 0.6981, ADA 0.6679, XRP 0.5995, DOGE 0.4451).

```bash
python scripts/data/correlation.py unity/Assets/StreamingAssets/Gym/data/{BTC,ETH,ADA,DOGE,XRP}USDT-1h.csv
```

Segments (in `gym-config.json`, the same dates for every coin): train 2017-08-17 to 2021-12-31 (for a coin listed later it starts on the day of its first candle: ADA 2018-04-17, XRP 2018-05-04, DOGE 2019-07-05), then four evaluation segments, which lie inside every coin's data: validation-bear 2022-01-01 to 2022-12-31 (BTC fell 64.5 %), validation-recovery 2023-01-01 to 2024-08-31 (BTC rose 256.8 %), validation 2024-09-01 to 2025-08-31 (BTC rose 83.7 %) and test 2025-09-01 to 2026-08-31. In the test segment BTC fell 27.4 %, so holding cash beats buy-and-hold there. The three validation segments are for choosing models; the test segment is kept for the final numbers. Round one trained on 2017-08-17 to 2024-08-31, with validation as its only validation segment. How the names are used: [evaluation.md](evaluation.md#segments).
