// Names of the map objectives (analysis.objectives) in Portuguese, shared by the lead chart and ObjectivesSection.

const LANES = { top: 'de cima', middle: 'do meio', bottom: 'de baixo' };
const TEAMS = { radiant: 'Radiant', dire: 'Dire' };

export const KIND_LABELS = {
    tower: 'Torre',
    barracks: 'Barracks',
    ancient: 'Ancient',
    roshan: 'Roshan',
    tormentor: 'Tormentor',
};

export function teamName(team) {
    return TEAMS[team] ?? team;
}

// "Torre T2 do meio", "Torre T4", "Barracks corpo a corpo de baixo", "Ancient", "Roshan".
export function objectiveName(objective) {
    const lane = LANES[objective.lane] ? ` ${LANES[objective.lane]}` : '';
    switch (objective.kind) {
        case 'tower':
            return `Torre T${objective.towerTier}${lane}`;
        case 'barracks':
            return `Barracks ${objective.meleeBarracks ? 'corpo a corpo' : 'à distância'}${lane}`;
        default:
            return KIND_LABELS[objective.kind] ?? objective.kind;
    }
}

// With the owner of a building: "Torre T2 do meio do Dire".
export function objectiveFullName(objective) {
    return objective.owner ? `${objectiveName(objective)} do ${teamName(objective.owner)}` : objectiveName(objective);
}
