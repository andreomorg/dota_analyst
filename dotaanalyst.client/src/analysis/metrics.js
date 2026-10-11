// Portuguese display names for the metric keys sent by the API.
const METRIC_LABELS = {
    goldPerMinute: 'Ouro por minuto (GPM)',
    experiencePerMinute: 'Experiência por minuto (XPM)',
    killsPerMinute: 'Abates por minuto',
    deathsPerMinute: 'Mortes por minuto',
    assistsPerMinute: 'Assistências por minuto',
    lastHitsPerMinute: 'Last hits por minuto',
    deniesPerMinute: 'Denies por minuto',
    heroDamagePerMinute: 'Dano a heróis por minuto',
    heroHealingPerMinute: 'Cura em heróis por minuto',
    towerDamage: 'Dano a torres',
};

const RATING_LABELS = {
    strong: 'Forte',
    average: 'Na média',
    weak: 'Fraco',
};

// Falls back to the raw key so a metric added later on the back-end still shows up.
export function metricLabel(metric) {
    return METRIC_LABELS[metric] ?? metric;
}

export function ratingLabel(rating) {
    return RATING_LABELS[rating] ?? rating;
}
