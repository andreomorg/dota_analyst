const ERROR_MESSAGES = {
    400: 'ID inválido. Use o account ID do Dota ou o SteamID64.',
    502: 'O OpenDota está indisponível no momento. Tente novamente em alguns minutos.',
};

// GETs a JSON resource, turning network/HTTP failures into errors with a Portuguese message.
// An aborted request rethrows the original AbortError so callers can ignore it.
async function getJson(url, signal, errorMessages, unexpectedError) {
    let response;
    try {
        response = await fetch(url, { signal });
    } catch (error) {
        if (signal?.aborted) {
            throw error;
        }
        throw new Error('Não foi possível conectar à API.', { cause: error });
    }

    if (!response.ok) {
        throw new Error(errorMessages[response.status] ?? unexpectedError);
    }

    try {
        return await response.json();
    } catch (error) {
        if (signal?.aborted) {
            throw error;
        }
        throw new Error(unexpectedError, { cause: error });
    }
}

export function fetchRecentMatches(id, signal) {
    return getJson(
        `/api/v1/players/${encodeURIComponent(id)}/matches`,
        signal,
        ERROR_MESSAGES,
        'Erro inesperado ao buscar as partidas.',
    );
}

export function fetchMatchAnalysis(accountId, matchId, signal) {
    return getJson(
        `/api/v1/players/${encodeURIComponent(accountId)}/matches/${encodeURIComponent(matchId)}`,
        signal,
        { ...ERROR_MESSAGES, 404: 'Partida não encontrada.' },
        'Erro inesperado ao buscar a análise da partida.',
    );
}
