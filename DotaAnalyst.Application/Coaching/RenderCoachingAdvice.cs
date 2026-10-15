using System.Globalization;
using DotaAnalyst.Domain.Coaching;

namespace DotaAnalyst.Application.Coaching;

// The model selects options; it cannot author factual claims. Text is localized here, outside the domain rules.
public static class RenderCoachingAdvice
{
    public static CoachingAdvice Render(CoachingContext context, IReadOnlyList<CoachingChoice> choices)
    {
        var errors = CoachingFindingSelector.ValidateChoices(context, choices);
        if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
        var eligible = CoachingFindingSelector.Select(context).ToDictionary(f => f.Id);
        return new(choices.Select(choice =>
        {
            var finding = eligible[choice.FindingId];
            double Value(string metric) => context.Evidence.Single(e => finding.EvidenceIds.Contains(e.Id) && e.Metric == metric).Value;
            string Number(double value) => value.ToString("0", CultureInfo.InvariantCulture);
            var (topic, observation) = (context.SchemaVersion, finding.Kind) switch
            {
                (4, CoachingFindingKind.IsolatedDeathsReview) => ("Revisar mortes sem aliados próximos",
                    $"Entre {Number(Value("ReviewedDeaths"))} mortes de 00:00 até o fim, {Number(Value("ConfirmedIsolatedDeaths"))} tiveram quatro aliados amostrados sem proximidade; em {Number(Value("UnknownIsolationDeaths"))}, o isolamento permaneceu desconhecido. Isso não comprova erro de decisão nem visão dos inimigos."),
                (4, CoachingFindingKind.FightProximityReview) => ("Revisar proximidade das lutas",
                    $"O detector encontrou {Number(Value("ReviewedFights"))} agrupamentos de mortes desde 00:00; em {Number(Value("ConfirmedDistantFights"))}, seu herói ficou distante do centro nas consultas cobertas da janela; em {Number(Value("UnknownFightProximity"))}, a proximidade permaneceu desconhecida. Distância não comprova oportunidade perdida, e confrontos sem mortes podem não aparecer."),
                (_, CoachingFindingKind.IsolatedDeathsReview) => ("Revisar mortes sem aliados próximos",
                    $"O cálculo identificou {Number(Value("IsolatedDeaths"))} de {Number(Value("Deaths"))} mortes sem aliado próximo na amostra. Isso não comprova que a decisão foi errada nem que os inimigos estavam visíveis."),
                (_, CoachingFindingKind.FightProximityReview) => ("Revisar proximidade das lutas",
                    $"O detector encontrou {Number(Value("TeamFights"))} lutas na partida; seu herói esteve próximo do centro amostrado de {Number(Value("FightsNearby"))}. Proximidade não comprova participação e estar longe pode ter sido uma troca adequada. Confrontos sem mortes podem não ter sido detectados."),
                (_, CoachingFindingKind.QuickRedeathsReview) => ("Revisar retornos após morrer",
                    $"O cálculo identificou {Number(Value("QuickRedeaths"))} mortes pouco depois do retorno estimado ao jogo. O retorno é aproximado pelas amostras e não demonstra erro por si só."),
                _ => throw new InvalidDataException("Unsupported finding.")
            };
            var (action, exercise) = Exercise(finding.Kind, finding.Role);
            IReadOnlyList<CoachingEpisode>? episodes = null;
            if (context.SchemaVersion >= 3)
            {
                episodes = (finding.EpisodeIds ?? []).Select(id => context.Episodes!.Single(e => e.Id == id)).ToArray();
                observation += episodes.Count == 0
                    ? " Este achado tem apenas o resumo agregado nesta versão."
                    : " Trechos localizados a partir das amostras: " + string.Join("; ", episodes.Select(e =>
                        $"{Clock(e.FromSeconds)}–{Clock(e.ToSeconds)} (evento em {Clock(e.EventSeconds)}{(e.Reincarnated ? "; reincarnação" : "")})")) + ".";
            }
            if (context.Role.IsInferred) observation += " A função foi inferida e precisa ser considerada com essa incerteza.";
            return new CoachingSuggestion(topic, observation, action, exercise,
                finding.EvidenceIds, finding.Id, choice.ExerciseId, episodes);
        }).ToArray());
    }

    private static string Clock(int seconds) =>
        (seconds / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);

    private static (string Action, string Exercise) Exercise(CoachingFindingKind kind, int role)
    {
        if (kind == CoachingFindingKind.FightProximityReview)
            return role switch
            {
                1 => ("Compare as lutas com as ações que você fazia em outro lugar.", "Em cada trecho, registre se estava obtendo farm, pressionando uma estrutura ou recuando e quais informações tinha para escolher. Não assuma que deveria ter ido à luta."),
                2 => ("Reveja suas rotações em relação às lutas detectadas.", "Compare sua rota e a posição dos aliados antes da luta. Anote o que sabia e o que fez em troca ao permanecer distante."),
                3 => ("Reveja sua posição antes das lutas e a pressão criada em outro local.", "Distinga oportunidades de aproximação de momentos em que segurar uma região ou pressionar outra rota era necessário."),
                _ => ("Reveja seu trajeto para apoiar os aliados nas lutas detectadas.", "Anote a distância inicial, aliados que podia acompanhar e se sua ação em outro local justificava ficar distante. Não atribua falta de comunicação sem evidência.")
            };
        if (kind == CoachingFindingKind.QuickRedeathsReview)
            return ("Reveja os deslocamentos após retornar ao jogo.", "Compare o caminho escolhido, a posição dos aliados e a informação disponível antes de se expor novamente. Distinga retorno necessário de exposição evitável.");
        return role switch
        {
            1 => ("Reveja os deslocamentos entre rotas e acampamentos antes das mortes.", "Em cada trecho, anote a posição dos aliados, a informação disponível e o que buscava obter. Compare uma opção de recuo sem assumir que a morte era evitável."),
            2 => ("Reveja as saídas da rota e os deslocamentos antes das mortes.", "Registre o objetivo da rotação, quem podia acompanhá-lo e quais informações estavam disponíveis ao decidir avançar."),
            3 => ("Reveja a exposição ao criar pressão antes das mortes.", "Distinga mortes que abriram espaço para o time de exposições sem ganho observável; registre a informação disponível antes de decidir."),
            4 => ("Reveja os deslocamentos de apoio antes das mortes.", "Anote com qual aliado pretendia jogar, onde ele estava e que informação sustentava o trajeto. Considere se a troca foi necessária."),
            _ => ("Reveja os reposicionamentos para apoiar a equipe antes das mortes.", "Registre a distância dos aliados e a informação disponível ao avançar ou recuar. Distinga uma troca necessária de exposição evitável, sem priorizar farm de carry.")
        };
    }
}
