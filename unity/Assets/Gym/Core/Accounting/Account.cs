using System;
using Gym.Core.Market;

namespace Gym.Core.Accounting
{
    /// <summary>
    /// 一个交易者的现金和 coin。coin 数量的除法和取整都用 decimal，这样 0.3 ÷ 0.1 正好等于 3。
    /// </summary>
    public sealed class Account
    {
        const double NegativeCashTolerance = 1e-9;
        /// <summary>取整让一次买入超出现金时，这次买入最多可以退回几个 step 单位。</summary>
        const int MaxUnitStepBacks = 3;

        public Account(SymbolRules rules, CostModel cost, double cash, long coinUnits = 0, double avgCost = 0)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Cost = cost ?? throw new ArgumentNullException(nameof(cost));
            if (!(cash >= 0) || double.IsInfinity(cash))
                throw new ArgumentOutOfRangeException(nameof(cash), cash, "Must be a finite value >= 0.");
            if (coinUnits < 0) throw new ArgumentOutOfRangeException(nameof(coinUnits), coinUnits, "Must be >= 0.");
            // 有持仓就必须有成本；成本为 0 时，算未实现收益率会除以零。
            if (coinUnits > 0 && (!(avgCost > 0) || double.IsInfinity(avgCost)))
                throw new ArgumentOutOfRangeException(nameof(avgCost), avgCost, "Must be a finite value > 0 when holding coin.");
            Cash = cash;
            CoinUnits = coinUnits;
            AvgCost = coinUnits == 0 ? 0 : avgCost;
        }

        public SymbolRules Rules { get; }
        public CostModel Cost { get; }

        public double Cash { get; private set; }
        public long CoinUnits { get; private set; }

        /// <summary>持仓里每个 coin 的平均成本，含买入时付的 fee。空仓时为 0。</summary>
        public double AvgCost { get; private set; }

        public int Trades { get; private set; }
        public int Rejected { get; private set; }
        public double FeesPaid { get; private set; }
        public double TurnoverNotional { get; private set; }

        public double Quantity => Rules.Quantity(CoinUnits);

        /// <summary>记下一笔没有到达账户的订单，例如被 action mask 挡掉的 action。</summary>
        public void RecordRejection() => Rejected++;

        /// <summary>拿现金的 <paramref name="fraction"/> 买入 coin，基准价格是 <paramref name="price"/>。</summary>
        public bool TryBuy(double fraction, double price, out Fill fill)
        {
            CheckArguments(fraction, price);
            fill = default;
            double budget = Cash * fraction;
            double fillPrice = price * (1 + Cost.Slippage);
            double spendable = budget - Cost.FixedFee;
            if (spendable <= 0) return Reject();

            decimal unitsExact = (decimal)spendable / ((decimal)fillPrice * (1m + (decimal)Cost.FeeRate)) / Rules.StepSize;
            long units = (long)decimal.Floor(unitsExact);
            double notional = Notional(units, fillPrice);
            double fee = notional * Cost.FeeRate + Cost.FixedFee;
            // double → decimal 只保留 15 位有效数字，所以现金上百万时，向下取整可能多出一个单位，
            // 超支几个 1e-9。这里把单位退回去；只有下面那个容差会拒绝的买入才会被改动。
            for (int i = 0; i < MaxUnitStepBacks && units > 0 && Cash - (notional + fee) < -NegativeCashTolerance; i++)
            {
                units--;
                notional = Notional(units, fillPrice);
                fee = notional * Cost.FeeRate + Cost.FixedFee;
            }
            if (units == 0 || notional < Rules.MinNotional) return Reject();

            double cash = Cash - (notional + fee);
            if (cash < 0)
            {
                if (cash < -NegativeCashTolerance)
                    throw new InvalidOperationException($"Buy would leave cash at {cash}.");
                cash = 0;
            }

            double oldQuantity = Quantity;
            long newUnits = CoinUnits + units;
            double newQuantity = Rules.Quantity(newUnits);
            AvgCost = (oldQuantity * AvgCost + notional + fee) / newQuantity;
            Cash = cash;
            CoinUnits = newUnits;
            Book(notional, fee);
            fill = new Fill(units, fillPrice, notional, fee);
            return true;
        }

        /// <summary>卖出 coin 的 <paramref name="fraction"/>，基准价格是 <paramref name="price"/>。</summary>
        public bool TrySell(double fraction, double price, out Fill fill)
        {
            CheckArguments(fraction, price);
            fill = default;
            long units = fraction >= 1 ? CoinUnits : (long)decimal.Floor(CoinUnits * (decimal)fraction);
            double fillPrice = price * (1 - Cost.Slippage);
            double notional = Notional(units, fillPrice);
            double fee = notional * Cost.FeeRate + Cost.FixedFee;
            if (units == 0 || notional < Rules.MinNotional || notional - fee <= 0) return Reject();

            Cash += notional - fee;
            CoinUnits -= units;
            if (CoinUnits == 0) AvgCost = 0;
            Book(notional, fee);
            fill = new Fill(units, fillPrice, notional, fee);
            return true;
        }

        public bool Buy(double fraction, double price) => TryBuy(fraction, price, out _);
        public bool Sell(double fraction, double price) => TrySell(fraction, price, out _);

        public double Equity(double price) => Cash + Quantity * price;

        public double PositionRatio(double price)
        {
            double equity = Equity(price);
            return equity > 0 ? Quantity * price / equity : 0;
        }

        /// <summary>假如整个持仓按 <paramref name="price"/> 卖掉，收益率是多少（算 fee，不算 slippage）。</summary>
        public double UnrealizedReturn(double price)
        {
            if (CoinUnits == 0) return 0;
            double quantity = Quantity;
            double cost = quantity * AvgCost;
            return (quantity * price * (1 - Cost.FeeRate) - Cost.FixedFee - cost) / cost;
        }

        double Notional(long units, double fillPrice) => (double)(units * Rules.StepSize * (decimal)fillPrice);

        bool Reject()
        {
            Rejected++;
            return false;
        }

        void Book(double notional, double fee)
        {
            Trades++;
            FeesPaid += fee;
            TurnoverNotional += notional;
        }

        static void CheckArguments(double fraction, double price)
        {
            if (!(fraction >= 0 && fraction <= 1))
                throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "Must be in [0, 1].");
            if (!(price > 0) || double.IsInfinity(price))
                throw new ArgumentOutOfRangeException(nameof(price), price, "Must be a finite value > 0.");
        }
    }
}
