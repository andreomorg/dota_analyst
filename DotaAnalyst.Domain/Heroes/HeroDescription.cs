using System.Globalization;

namespace DotaAnalyst.Domain.Heroes;

// A reproducible factual projection, not lore, strategy or a prediction. Persist the version alongside the text.
public sealed record HeroDescription(string Language, int Version, string Text)
{
    public static HeroDescription? From(HeroDetails details)
    {
        if (details.Profile is not { Attributes: not null, Attack: not null, Defense: not null, Vitals: not null, Movement: not null } p)
            return null;
        static string N(HeroNumber? number) => number?.Value.ToString("0.####", CultureInfo.GetCultureInfo("pt-BR")) ?? "não informado";
        var attack = details.AttackType == HeroAttackType.Melee ? "corpo a corpo" : "à distância";
        var attribute = details.PrimaryAttribute switch
        {
            HeroAttribute.Strength => "força", HeroAttribute.Agility => "agilidade",
            HeroAttribute.Intelligence => "inteligência", _ => "universal",
        };
        return new("pt-BR", 1,
            $"Herói {attack}, de atributo {attribute}. " +
            $"Papéis do catálogo: {string.Join(", ", details.Roles.OrderBy(role => role).Select(role => role switch { HeroRole.Carry => "carregador", HeroRole.Support => "suporte", HeroRole.Nuker => "dano explosivo", HeroRole.Disabler => "controle", HeroRole.Initiator => "iniciador", HeroRole.Escape => "fuga", HeroRole.Durable => "resistente", HeroRole.Pusher => "pressão de estruturas", _ => "selva" }))}. " +
            $"Atributos base: força {N(p.Attributes.Strength)}, agilidade {N(p.Attributes.Agility)}, inteligência {N(p.Attributes.Intelligence)}. " +
            $"Ganhos por nível (força/agilidade/inteligência): {N(p.Attributes.StrengthGain)}/{N(p.Attributes.AgilityGain)}/{N(p.Attributes.IntelligenceGain)}. " +
            $"Dano base configurado: {N(p.Attack.DamageMin)} a {N(p.Attack.DamageMax)}; alcance: {N(p.Attack.Range)} unidades; tempo base de ataque: {N(p.Attack.BaseAttackTime)} s. " +
            $"Armadura base: {N(p.Defense.Armor)}; resistência mágica base: {N(p.Defense.MagicResistance)}%. " +
            "Configuração nominal do catálogo; não representa os totais efetivos com atributos, itens e efeitos da partida.");
    }
}
