using Shouldly;
using Synapse.Brain.SpacedRepetition;

namespace Synapse.Tests.Brain;

public class Sm2EngineTests
{
    [Fact]
    public void Evaluate_WhenGradeIsFive_ShouldSetNextIntervalAndIncreaseRepetition()
    {
        var state = new Sm2State { RepetitionNumber = 0, EaseFactor = 2.5f, IntervalDays = 0 };
        var now = DateTimeOffset.UtcNow;

        var nextState = Sm2Engine.Evaluate(state, 5, now);

        nextState.RepetitionNumber.ShouldBe(1);
        nextState.IntervalDays.ShouldBe(1);
        nextState.EaseFactor.ShouldBeGreaterThan(2.5f);
        nextState.NextReviewDate.ShouldBe(now.AddDays(1));
    }

    [Fact]
    public void Evaluate_SecondRepetitionWithGoodGrade_ShouldSetIntervalToSixDays()
    {
        var state = new Sm2State { RepetitionNumber = 1, EaseFactor = 2.5f, IntervalDays = 1 };
        var now = DateTimeOffset.UtcNow;

        var nextState = Sm2Engine.Evaluate(state, 4, now);

        nextState.RepetitionNumber.ShouldBe(2);
        nextState.IntervalDays.ShouldBe(6);
    }

    [Fact]
    public void Evaluate_WhenFailedGrade_ShouldResetRepetitionsToZero()
    {
        var state = new Sm2State { RepetitionNumber = 4, EaseFactor = 2.5f, IntervalDays = 30 };
        var now = DateTimeOffset.UtcNow;

        var nextState = Sm2Engine.Evaluate(state, 1, now);

        nextState.RepetitionNumber.ShouldBe(0);
        nextState.IntervalDays.ShouldBe(1);
        nextState.EaseFactor.ShouldBeLessThan(2.5f);
    }

    [Fact]
    public void Evaluate_EaseFactorShouldNeverDropBelowMinimum()
    {
        var state = new Sm2State { RepetitionNumber = 0, EaseFactor = 1.3f, IntervalDays = 0 };
        var now = DateTimeOffset.UtcNow;

        var nextState = Sm2Engine.Evaluate(state, 0, now);

        nextState.EaseFactor.ShouldBe(Sm2Engine.MinEaseFactor);
    }
}
