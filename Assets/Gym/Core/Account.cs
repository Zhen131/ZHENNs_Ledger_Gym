using System;

namespace Gym.Core
{
    /// <summary>A filled order as the account booked it.</summary>
    public readonly struct Fill
    {
        public readonly long Units;
        /// <summary>Fill price after slippage.</summary>
        public readonly double Price;
        /// <summary>Order value N = units × step × price, before fees.</summary>
        public readonly double Notional;
        /// <summary>Fee F = N × FeeRate + FixedFee.</summary>
        public readonly double Fee;

        public Fill(long units, double price, double notional, double fee)
        {
            Units = units;
            Price = price;
            Notional = notional;
            Fee = fee;
        }
    }

    /// <summary>
    /// Cash and coin of one trader, with the bookkeeping rules of the contract
    /// (01B §2.2). Division and rounding of coin amounts use decimal so that
    /// 0.3 ÷ 0.1 is exactly 3.
    /// </summary>
    public sealed class Account
    {
        const double NegativeCashTolerance = 1e-9;
        /// <summary>How many step units a buy may give back when rounding left it over the cash (01D-1).</summary>
        const int MaxUnitStepBacks = 3;

        public Account(SymbolRules rules, CostModel cost, double cash, long coinUnits = 0, double avgCost = 0)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Cost = cost ?? throw new ArgumentNullException(nameof(cost));
            if (!(cash >= 0) || double.IsInfinity(cash))
                throw new ArgumentOutOfRangeException(nameof(cash), cash, "Must be a finite value >= 0.");
            if (coinUnits < 0) throw new ArgumentOutOfRangeException(nameof(coinUnits), coinUnits, "Must be >= 0.");
            // A position needs a cost; with 0 the unrealised return divides by zero (01D-2).
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

        /// <summary>Average cost per coin of the position, including buy fees. 0 when flat.</summary>
        public double AvgCost { get; private set; }

        public int Trades { get; private set; }
        public int Rejected { get; private set; }
        public double FeesPaid { get; private set; }
        public double TurnoverNotional { get; private set; }

        public double Quantity => Rules.Quantity(CoinUnits);

        /// <summary>Counts an order that never reached the account, e.g. a masked action.</summary>
        public void RecordRejection() => Rejected++;

        /// <summary>Spend <paramref name="fraction"/> of the cash on coin at base price <paramref name="price"/>.</summary>
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
            // double → decimal keeps 15 significant digits, so with cash in the millions the floor
            // can land a unit too high and overspend by a few 1e-9. Give units back (01D-1); only
            // buys the tolerance below would refuse are changed.
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

        /// <summary>Sell <paramref name="fraction"/> of the coin at base price <paramref name="price"/>.</summary>
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

        /// <summary>Return if the whole position were sold at <paramref name="price"/> (fees included, slippage not).</summary>
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
