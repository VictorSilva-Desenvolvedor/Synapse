namespace Synapse.Brain.Services;

/// <summary>
/// Operações matemáticas e cálculo de similaridade de cossenos para vetores de embeddings.
/// </summary>
public static class VectorMath
{
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0f;
        }

        var dotProduct = 0f;
        var magnitudeA = 0f;
        var magnitudeB = 0f;

        for (var i = 0; i < a.Length; i++)
        {
            var valA = a[i];
            var valB = b[i];

            dotProduct += valA * valB;
            magnitudeA += valA * valA;
            magnitudeB += valB * valB;
        }

        var magnitude = MathF.Sqrt(magnitudeA) * MathF.Sqrt(magnitudeB);
        if (magnitude <= 0.000001f)
        {
            return 0f;
        }

        return dotProduct / magnitude;
    }
}
