using Shouldly;
using Synapse.Brain.Services;

namespace Synapse.Tests.Brain;

public class VectorMathTests
{
    [Fact]
    public void CosineSimilarity_WithIdenticalVectors_ShouldReturnOne()
    {
        var v1 = new float[] { 1f, 2f, 3f };
        var v2 = new float[] { 1f, 2f, 3f };

        var similarity = VectorMath.CosineSimilarity(v1, v2);

        similarity.ShouldBeInRange(0.999f, 1.001f);
    }

    [Fact]
    public void CosineSimilarity_WithOrthogonalVectors_ShouldReturnZero()
    {
        var v1 = new float[] { 1f, 0f, 0f };
        var v2 = new float[] { 0f, 1f, 0f };

        var similarity = VectorMath.CosineSimilarity(v1, v2);

        similarity.ShouldBeInRange(-0.001f, 0.001f);
    }

    [Fact]
    public void CosineSimilarity_WithOppositeVectors_ShouldReturnMinusOne()
    {
        var v1 = new float[] { 1f, 2f, 3f };
        var v2 = new float[] { -1f, -2f, -3f };

        var similarity = VectorMath.CosineSimilarity(v1, v2);

        similarity.ShouldBeInRange(-1.001f, -0.999f);
    }
}
