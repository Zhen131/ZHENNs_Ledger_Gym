using System;
using Gym.Core.Env;

namespace Gym.Runtime.Play
{
    /// <summary>HUD 显示的数字，每个 step 之后立刻抓取。</summary>
    public struct HudSnapshot
    {
        public bool Ready;
        /// <summary>币对名（例如 BTCUSDT），持币那一行由它推出基础资产名。</summary>
        public string Symbol;
        public DateTime TimeUtc;
        public double Close;
        public double Cash;
        public long CoinUnits;
        public double Quantity;
        public double PositionRatio;
        public double Equity;
        public double Return;
        public double FeesPaid;
        public int Trades;
        public int Rejected;
        public string LastAction;
        public double FeeRate;
        public double FixedFee;
        public double Slippage;
        public int Step;

        /// <summary>这个 episode 里走过 step 没有；没走过时下面三项没有意义。</summary>
        public bool HasStepped;
        /// <summary>上一步选的是买、卖还是不动。<see cref="LastAction"/> 是同一件事的英文说法，画面上的字按语言另外生成。</summary>
        public TradeAction LastSide;
        /// <summary>上一步的下单比例（0～1）。</summary>
        public double LastFraction;
        /// <summary>上一步的买入或卖出成交了没有。</summary>
        public bool LastFilled;
        /// <summary>账户的已实现盈亏（USDT）。</summary>
        public double RealizedPnl;
        /// <summary>按当前这根的 close 算的未实现盈亏（USDT）。</summary>
        public double UnrealizedPnl;

        /// <summary>环境是不是旋钮模式；为 false（下单模式）时下面两项没有意义。</summary>
        public bool DialMode;
        /// <summary>旋钮模式下最近一次选的目标仓位（0～1）；这一局还没决策时为 −1。</summary>
        public double DialTarget;
        /// <summary>旋钮模式下这一局换档的次数（<see cref="Gym.Core.Env.TradingEnv.DialChanges"/>）。</summary>
        public int DialChanges;
    }
}
