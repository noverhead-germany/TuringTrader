//==============================================================================
// Project:     TuringTrader, algorithms from books & publications
// Name:        Ehlers_RocketScienceForTraders_v2.cs
// Description: Strategies as published by John F Ehlers
//==============================================================================

#region libraries
using System;
using System.Collections.Generic;
using TuringTrader.GlueV2;
using TuringTrader.Optimizer;
using TuringTrader.SimulatorV2;
using TuringTrader.SimulatorV2.Assets;
using TuringTrader.SimulatorV2.Indicators;
#endregion

namespace TuringTrader.BooksAndPubsV2
{
    /// <summary>
    /// TradeStation compatibility helpers (degrees-based trig)
    /// </summary>
    internal static class TradeStationCompat
    {
        private static double DegToRad(double deg) => deg * Math.PI / 180.0;
        public static double Cosine(double deg) => Math.Cos(DegToRad(deg));
        public static double Sine(double deg) => Math.Sin(DegToRad(deg));
    }

    #region correlation as a trend indicator
    public abstract class Ehlers_TechnicalPapers_CorrelationAsTrendIndicator_Core : Algorithm
    {
        public override string Name => "Ehlers' Correlation As A Trend Indicator";

        public virtual object ASSET { get; set; } = ETF.SPY;
        [OptimizerParam(0, 1, 1)] public virtual int ALLOW_LONG { get; set; } = 1;
        [OptimizerParam(0, 1, 1)] public virtual int ALLOW_SHORT { get; set; } = 0;
        [OptimizerParam(63, 252, 21)] public virtual int CORR_PERIOD { get; set; } = 252;
        [OptimizerParam(20, 95, 5)] public virtual int CORR_ENTRY { get; set; } = 50;
        [OptimizerParam(0, 75, 5)] public virtual int CORR_EXIT { get; set; } = 0;

        public override bool IsOptimizerParamsValid => CORR_ENTRY >= CORR_EXIT;

        public override void Run()
        {
            StartDate = StartDate ?? DateTime.Parse("1970-01-01T16:00-05:00");
            EndDate = EndDate ?? AlgorithmConstants.END_DATE - TimeSpan.FromDays(5);
            WarmupPeriod = TimeSpan.FromDays(365);
            ((Account_Default)Account).Friction = AlgorithmConstants.FRICTION;

            var asset = Asset(ASSET);

            var ramp = Asset("ramp", () =>
            {
                var bars = new List<BarType<OHLCV>>();
                var y = 0.0;
                foreach (var t in TradingCalendar.TradingDays)
                {
                    y += 1.0;
                    bars.Add(new BarType<OHLCV>(t, new OHLCV(y, y, y, y, 0.0)));
                }
                return bars;
            });

            SimLoop(() =>
            {
                var correlation = asset.Close.Correlation(ramp.Close, CORR_PERIOD);

                var targetAllocation = Lambda("allocation", prev =>
                {
                    if (prev <= 0.0 && correlation[0] > CORR_ENTRY / 100.0)
                        return ALLOW_LONG != 0 ? 1.0 : 0.0;

                    if (prev >= 0.0 && correlation[0] < -CORR_ENTRY / 100.0)
                        return ALLOW_SHORT != 0 ? -1.0 : 0.0;

                    if ((prev > 0.0 && correlation[0] < CORR_EXIT / 100.0) ||
                        (prev < 0.0 && correlation[0] > -CORR_EXIT / 100.0))
                        return 0.0;

                    return prev;
                }, 0.0)[0];

                if (Math.Abs(targetAllocation - asset.Position) > 0.10)
                    asset.Allocate(targetAllocation, OrderType.openNextBar);
            });
        }
    }
    #endregion

    #region correlation as a cycle indicator
    public abstract class Ehlers_TechnicalPapers_CorrelationAsCycleIndicator_Core : Algorithm
    {
        public override string Name => "Ehlers' Correlation As A Cycle Indicator";

        public virtual object ASSET { get; set; } = ETF.SPY;
        [OptimizerParam(0, 1, 1)] public virtual int ALLOW_LONG { get; set; } = 1;
        [OptimizerParam(0, 1, 1)] public virtual int ALLOW_SHORT { get; set; } = 1;
        [OptimizerParam(21, 252, 21)] public virtual int PERIOD { get; set; } = 30;
        [OptimizerParam(21, 252, 21)] public virtual int MAX_CYCLE { get; set; } = 60;

        public override void Run()
        {
            StartDate = StartDate ?? DateTime.Parse("1970-01-01T16:00-05:00");
            EndDate = EndDate ?? AlgorithmConstants.END_DATE - TimeSpan.FromDays(5);
            WarmupPeriod = TimeSpan.FromDays(90);
            ((Account_Default)Account).Friction = AlgorithmConstants.FRICTION;

            var asset = Asset(ASSET);
            var lbg = new LookbackGroup();
            var Signal = lbg.NewLookback(0);
            var Real = lbg.NewLookback(0);
            var Imag = lbg.NewLookback(0);
            var Angle = lbg.NewLookback(0);

            SimLoop(() =>
            {
                lbg.Advance();
                Signal.Value = asset.TypicalPrice()[0];

                double sx = 0, sy = 0, sxx = 0, sxy = 0, syy = 0;

                for (var i = 1; i <= PERIOD; i++)
                {
                    var x = Signal[i - 1];
                    var y = TradeStationCompat.Cosine(360.0 * (i - 1) / PERIOD);
                    sx += x; sy += y; sxx += x * x; sxy += x * y; syy += y * y;
                }

                if (PERIOD * sxx - sx * sx > 0 && PERIOD * syy - sy * sy > 0)
                    Real.Value = (PERIOD * sxy - sx * sy) /
                                 Math.Sqrt((PERIOD * sxx - sx * sx) * (PERIOD * syy - sy * sy));

                sx = sy = sxx = sxy = syy = 0;

                for (var i = 1; i <= PERIOD; i++)
                {
                    var x = Signal[i - 1];
                    var y = -TradeStationCompat.Sine(360.0 * (i - 1) / PERIOD);
                    sx += x; sy += y; sxx += x * x; sxy += x * y; syy += y * y;
                }

                if (PERIOD * sxx - sx * sx > 0 && PERIOD * syy - sy * sy > 0)
                    Imag.Value = (PERIOD * sxy - sx * sy) /
                                 Math.Sqrt((PERIOD * sxx - sx * sx) * (PERIOD * syy - sy * sy));

                Angle.Value = 90.0 - 180.0 / Math.PI * Math.Atan2(Imag, Real);
            });
        }
    }
    #endregion
}
//==============================================================================
// end of file
