namespace Synapse.Brain.SpacedRepetition;

/// <summary>
/// Implementação do algoritmo matemático clássico SM-2 (SuperMemo 2) para repetição espaçada.
/// </summary>
public static class Sm2Engine
{
    public const float MinEaseFactor = 1.3f;
    public const float DefaultEaseFactor = 2.5f;

    /// <summary>
    /// Avalia um flashcard e calcula o próximo estado de retenção.
    /// </summary>
    /// <param name="current">Estado atual do card.</param>
    /// <param name="grade">Classificação de 0 a 5 (0: Falha total, 3: Correto com esforço, 5: Perfeito e fácil).</param>
    /// <param name="reviewDate">Data em que a revisão foi realizada.</param>
    public static Sm2State Evaluate(Sm2State current, int grade, DateTimeOffset reviewDate)
    {
        ArgumentNullException.ThrowIfNull(current);
        grade = Math.Clamp(grade, 0, 5);

        var nextState = new Sm2State
        {
            LastReviewedDate = reviewDate
        };

        // 1. Cálculo de repetições e intervalo
        if (grade >= 3)
        {
            if (current.RepetitionNumber == 0)
            {
                nextState.IntervalDays = 1;
            }
            else if (current.RepetitionNumber == 1)
            {
                nextState.IntervalDays = 6;
            }
            else
            {
                nextState.IntervalDays = (int)MathF.Round(current.IntervalDays * current.EaseFactor);
                if (nextState.IntervalDays <= current.IntervalDays)
                {
                    nextState.IntervalDays = current.IntervalDays + 1;
                }
            }

            nextState.RepetitionNumber = current.RepetitionNumber + 1;
        }
        else
        {
            // Falha na lembrança: reinicia repetição para 0 e intervalo para 1 dia
            nextState.RepetitionNumber = 0;
            nextState.IntervalDays = 1;
        }

        // 2. Ajuste do fator de facilidade (Ease Factor)
        var newEf = current.EaseFactor + (0.1f - (5 - grade) * (0.08f + (5 - grade) * 0.02f));
        nextState.EaseFactor = Math.Max(newEf, MinEaseFactor);

        // 3. Próxima data de revisão
        nextState.NextReviewDate = reviewDate.AddDays(nextState.IntervalDays);

        return nextState;
    }
}
