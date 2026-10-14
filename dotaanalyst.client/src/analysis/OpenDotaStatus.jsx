const messages = {
    queued: ['Solicitação ao OpenDota na fila', 'O processamento será solicitado automaticamente. Os dados disponíveis continuam abaixo.'],
    processing: ['OpenDota processando a partida', 'A solicitação foi aceita. Vamos atualizar os dados quando estiverem disponíveis.'],
    refreshing: ['Atualizando dados do OpenDota', 'Estamos verificando se os dados processados já estão disponíveis.'],
    deferred: ['Solicitação ao OpenDota adiada', 'O serviço não pode atender agora. Uma nova verificação será feita depois.'],
    failed: ['OpenDota não pôde processar a partida', 'Os dados disponíveis e a análise do nosso replay continuam preservados.'],
    unavailable: ['Processamento OpenDota indisponível', 'O OpenDota ainda não processou esta partida. Os percentis podem faltar.'],
    delayed: ['Ainda aguardando o OpenDota', 'O acompanhamento automático desta página terminou. Reabra a partida depois para consultar os dados.'],
};

export default function OpenDotaStatus({ analysis, pollingStopped }) {
    if (analysis.isParsed && (!analysis.openDotaProcessing?.enrichmentOnly || analysis.openDotaProcessing.status === 'ready')) return null;
    const status = pollingStopped ? 'delayed' : analysis.openDotaProcessing?.status ?? 'unavailable';
    const [title, text] = messages[status] ?? messages.unavailable;
    return <div className="notice" role="status" aria-live="polite" data-opendota-state={status}>
        <strong>{title}</strong><br />{text}
    </div>;
}
