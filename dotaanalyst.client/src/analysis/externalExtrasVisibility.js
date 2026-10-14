export function hasOpenDotaExtras(extras, section) {
    if (!extras) return false;
    return section === 'combat' ? extras.damageReceived != null : extras.abilityUses != null || extras.itemUses != null
        || extras.actionsPerMinute != null || extras.goldSources != null || extras.experienceSources != null;
}
