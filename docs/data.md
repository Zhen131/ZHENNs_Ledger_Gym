# Data and licence

**Data: Binance Vision.** The environment replays BTC/USDT spot 1-hour candles from [Binance Vision](https://data.binance.vision), from 2017-08-17 04:00 UTC to 2026-08-31 23:00 UTC. The processed file is committed at `unity/Assets/StreamingAssets/Gym/data/BTCUSDT-1h.csv`, next to a manifest with the SHA-256 of every source archive and of the CSV itself.

The data is licensed [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/) under the [Binance Vision Dataset Terms v1.0 (2026-08-26)](https://github.com/binance/binance-public-data/blob/master/TERMS_AND_CONDITIONS.md); see [`DATA-LICENSE.md`](../unity/Assets/StreamingAssets/Gym/data/DATA-LICENSE.md) for the attribution and the list of changes. This project is not affiliated with, sponsored or endorsed by Binance. The code has no licence file yet.

To regenerate the data (standard library only; raw archives are cached in `data/raw/`, which Git ignores):

```bash
python scripts/data/fetch_binance_klines.py --self-test
python scripts/data/fetch_binance_klines.py --symbol BTCUSDT
```

By default the script stops at 2026-08, the end of the test segment (pass `--end YYYY-MM` to add newer months), and drops the 43 candles of February 2018 that start at hh:28 instead of on the hour, forward-filling those hours (`--off-hour error` stops instead, `--off-hour floor` moves them to the hour). So the default command rebuilds exactly the committed file: the `csv_sha256` in the manifest is `4739c139dc501e38498589359db093394dcee86cabde5a6e37c12d726d084242`.

When the archives are downloaded afresh (another machine, an empty `data/raw/`), the CSV is the same and the manifest differs only in `downloaded_at_utc`, the time the archives were saved; do not commit that change. The script prints a `!!! WARNING` when the result has off-hour candles or gaps over 24 hours that the committed data does not have (it has 43 off-hour candles and one 75-hour gap from 2018-02-08 01:00), so a new stretch of flat filler from a later `--end` does not go unnoticed.

Segments (in `gym-config.json`): train 2017-08-17 to 2024-08-31, validation 2024-09-01 to 2025-08-31, test 2025-09-01 to 2026-08-31. In the test segment BTC fell 27.4 %, so holding cash beats buy-and-hold there.
