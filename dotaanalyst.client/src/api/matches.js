const ERROR_MESSAGES = {
    400: 'ID inválido. Use o account ID do Dota ou o SteamID64.',
    502: 'O OpenDota está indisponível no momento. Tente novamente em alguns minutos.',
};

const UNEXPECTED_ERROR = 'Erro inesperado ao buscar as partidas.';

export async function fetchRecentMatches(id, signal) {
    let response;
    try {
        response = await fetch(`/api/players/${encodeURIComponent(id)}/matches`, { signal });
    } catch (error) {
        if (signal?.aborted) {
            throw error;
        }
        throw new Error('Não foi possível conectar à API.', { cause: error });
    }

    if (!response.ok) {
        throw new Error(ERROR_MESSAGES[response.status] ?? UNEXPECTED_ERROR);
    }

    try {
        return await response.json();
    } catch (error) {
        if (signal?.aborted) {
            throw error;
        }
        throw new Error(UNEXPECTED_ERROR, { cause: error });
    }
}
