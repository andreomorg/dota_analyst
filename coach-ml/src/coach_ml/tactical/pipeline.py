"""Resumable development inference. Never approves training labels or installs weights."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import time

from ..core.json import canonical, parse_json, digest
from ..core.validation import require, AuditError
from ..reviews.upstream import JsonHttpClient
from .contracts import validate_candidate
from .projection import project_scene

EXAMPLE = {'schemaVersion': 1, 'disposition': 'conditional-review',
           'opportunity': 'Pergunta específica de revisão da janela.', 'evidenceIds': ['positions'],
           'alternatives': [
               {'id': 'A', 'action': 'Comparar aproximação ao grupo.',
                'conditions': ['Conferir caminho e condições de intervenção.'],
                'evidenceIds': ['positions'], 'unknowns': ['visibility', 'safe-path']},
               {'id': 'B', 'action': 'Comparar manutenção de posição.',
                'conditions': ['Conferir a utilidade da posição e risco de deslocamento.'],
                'evidenceIds': ['positions'], 'unknowns': ['visibility']}],
           'preference': 'undetermined', 'exercise': {
               'steps': ['Pause na âncora e compare as condições de cada alternativa.'],
               'completionCriterion': 'Condições presentes e desconhecidas registradas para as duas opções.',
               'evidenceIds': ['positions']},
           'limitations': ['Posições são oniscientes e não demonstram visão, recursos ou intenção.']}

SYSTEM = '''Você é um coach de Dota revisando somente informações disponíveis na âncora.
Responda em português com JSON estrito no contrato fornecido. Não use conhecimento de resultado futuro.
Não invente visão, HP/mana, cooldown, inventário, prontidão, intenções ou mecânicas de patch.
Procure uma pergunta específica e útil, duas alternativas defensáveis e condições que distinguem opções.
Não conclua que o jogador deveria lutar só por haver aliados observados. Não confunda compra com item disponível.
disposition: conditional-review para hipótese condicionada (preference=undetermined); insufficient quando
nem uma comparação específica for sustentada (alternatives=[] e preference=undetermined);
supported-review com preference A ou B somente se evidências realmente distinguirem as opções.
Alternativas não vazias são exatamente duas, IDs A e B, cada uma com action, conditions, evidenceIds, unknowns.
Use apenas evidenceIds e unknowns existentes na entrada. Não copie números desnecessariamente.
opportunity/action até 400 caracteres; condições 1–4 até 400 cada; steps 1–3 até 300 cada;
completionCriterion até 400; limitations 1–8 até 700 cada. Não inclua campos novos.
O exemplo define formato, não é resposta universal. Evite citar nomes de habilidades/itens ou locais não observados.
Não responda com o texto genérico "Pergunta específica de revisão da janela" nem apenas "compare as condições".
Conecte a oportunidade a pelo menos um fato concreto da entrada e explique por que esse fato merece revisão.
Contagem de aliados observados não informa proximidade; use coordenadas apenas como posições registradas,
sem calcular distâncias ou afirmar segurança de caminho. Não afirme que o jogador está agrupado só por haver amostras.
Se existir support-transition, use a mudança canônica de separação e seus extremos para tornar a revisão concreta.
Uma separação diminuída não prova que alguém podia intervir, nem quem se moveu; confira isso no exercício.
Não nomeie heróis por memória do ID, não chame um inimigo de vulnerável e não invente torre próxima ou vantagem de itens.
Condições devem ser verificações explícitas ("verificar se..."), nunca afirmações de recursos/segurança já disponíveis.
Um exercício deve dizer o que registrar e como encerrar a conferência; "avaliar a melhor opção" sozinho não basta.
Se os fatos só permitem uma pergunta condicionada, diga isso e torne o exercício específico à evidência existente.
Contrato de saída (somente estrutura; escreva seus próprios textos):
schemaVersion:1; disposition:string; opportunity:string; evidenceIds:string[];
alternatives:[{id:"A",action:string,conditions:string[],evidenceIds:string[],unknowns:string[]},
{id:"B",action:string,conditions:string[],evidenceIds:string[],unknowns:string[]}];
preference:string; exercise:{steps:string[],completionCriterion:string,evidenceIds:string[]}; limitations:string[].'''


def load_cases(source, index, include_support=False, partition='development'):
    source = source.resolve()
    manifest = parse_json((source/'manifest.json').read_text(encoding='utf-8-sig'))
    matches = {str(row['matchId']): row for row in manifest['matches']}
    allowed = {'development':{'train','train-known'}, 'validation-diagnostic':{'validation-new','validation-known'}}
    require(partition in allowed, 'Unsupported tactical partition; reserved data stays closed')
    records = []
    for line in index.read_text(encoding='utf-8').splitlines():
        record = parse_json(line)
        require(record['sourceSplit'] in allowed[partition], 'Tactical runner excludes this partition')
        declared = matches.get(record['matchId'])
        require(declared is not None and declared['split'] == record['sourceSplit']
                and declared['sourceHash'] == record['sourceMatchHash'], 'Manifest identity mismatch')
        path = (source/record['sourcePath']).resolve()
        require(path.is_relative_to(source/'decisions'), 'Source outside decision directory')
        raw = path.read_bytes()
        require(hashlib.sha256(raw).hexdigest() == record['sourceDocumentSha256'], 'Source hash changed')
        context = parse_json(raw.decode('utf-8-sig'))
        require(str(context['matchId']) == record['matchId'] and context['heroId'] == record['heroId'], 'Source identity mismatch')
        scenes = [s for s in context['scenes'] if s['retrospectiveEpisode']['id'] == record['sceneId']]
        require(len(scenes) == 1, 'Missing or duplicate scene')
        request = project_scene(context, scenes[0])
        if include_support:
            from .support import add_support
            checkpoint = parse_json((source/'completed'/(record['matchId']+'.json')).read_text(encoding='utf-8-sig'))
            require(checkpoint['sourceHash'] == record['sourceMatchHash'], 'Support checkpoint source mismatch')
            perspectives = [p for p in checkpoint['perspectives'] if p['heroId'] == record['heroId']]
            require(len(perspectives) == 1, 'Support perspective mismatch')
            perspective = perspectives[0]
            temporal_path = (source/perspective['temporalFile']).resolve()
            require(temporal_path.is_relative_to(source/'temporal'), 'Support source outside temporal directory')
            temporal_bytes = temporal_path.read_bytes()
            require(hashlib.sha256(temporal_bytes).hexdigest() == perspective['temporalHash'], 'Support source hash changed')
            transitions = [t for batch in parse_json(temporal_bytes.decode('utf-8-sig')) for t in batch['provenance']
                           if t['episodeId'] == record['sceneId']]
            # Export batches must not repeat a source episode in their provenance.
            require(len(transitions) == 1, 'Missing or repeated support transition')
            add_support(request, transitions[0])
            record = {**record, 'supportSourceSha256': perspective['temporalHash']}
        require(request['anchorSeconds'] == record['anchorSeconds'], 'Changed anchor')
        records.append((record, request))
    require(len({r['caseId'] for r, _ in records}) == len(records), 'Duplicate case IDs')
    require(len({digest(q) for _, q in records}) == len(records), 'Duplicate final projected inputs')
    return records


def load_selection(source, index=None, bundle=None, include_support=False, partition='development', actions=None, patches=None):
    require(actions is None or partition == 'development', 'Action receipts are development-only')
    require(patches is None or partition == 'development', 'Patch receipts are development-only')
    if bundle is not None:
        from .bundles import load_bundle
        require(actions is None, 'Control bundles require a separately exported action receipt')
        require(patches is None, 'Control bundles require a separately exported patch receipt')
        require(index is None and not include_support, 'Control bundles do not contain support transitions')
        return load_bundle(source,bundle,partition)
    require(index is not None, 'Source index or audited control bundle required')
    cases = load_cases(source,index,include_support,partition)
    if actions is not None:
        from .actions import attach_actions
        cases = attach_actions(cases,actions,source,index)
    if patches is not None:
        from .patches import attach_patches
        cases = attach_patches(cases,patches,source,index)
    return cases


def parse_candidate(request, raw):
    try:
        value = parse_json(raw)
        validate_candidate(request, value)
        return {'valid': True, 'candidate': value, 'error': None}
    except (AuditError, KeyError, TypeError) as exc:
        return {'valid': False, 'candidate': None, 'error': str(exc)}


def score_generation(request, generated):
    result = parse_candidate(request, generated['text'])
    if generated.get('stopReason') == 'length':
        return {**result, 'valid': False, 'error': 'token-limit'}
    if result['valid'] and 'boundedFactsPolicy' in generated:
        from .bounded import POLICIES,validate_bounded
        try:
            require(generated['boundedFactsPolicy'] in POLICIES,'Unknown bounded facts policy')
            validate_bounded(request,result['candidate'],generated['boundedFactsPolicy'])
        except AuditError as exc:
            return {**result,'valid':False,'error':str(exc)}
    return result


def _run_cases(cases, output, identity, generate, scorer, system):
    output.mkdir(parents=True, exist_ok=True)
    run_identity = {'schemaVersion': 1, 'engine': identity, 'systemSha256': digest(system),
                    'runtimeCodeSha256': {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                                          for p in sorted(Path(__file__).parent.glob('*.py'))},
                    'cases': [{'caseId': r['caseId'], 'inputSha256': digest(q),
                               'supportSourceSha256': r.get('supportSourceSha256'),
                               **({'actionSourceSha256':r['actionSourceSha256']} if 'actionSourceSha256' in r else {}),
                               **({'patchSourceSha256':r['patchSourceSha256']} if 'patchSourceSha256' in r else {})}
                              for r, q in cases],
                    'tacticalQualityApproved': False, 'humanReviewed': False}
    identity_file, predictions_file = output/'identity.json', output/'predictions.jsonl'
    if identity_file.exists():
        require(parse_json(identity_file.read_text()) == run_identity, 'Cannot resume with changed inputs or engine')
    else:
        require(not any(p.name != '.tactical.lock' for p in output.iterdir()), 'Output directory is not empty')
        identity_file.write_text(canonical(run_identity)+'\n', encoding='utf-8')
    completed = []
    if predictions_file.exists():
        raw = predictions_file.read_bytes()
        require(not raw or raw.endswith(b'\n'), 'Interrupted prediction line; preserve for investigation')
        for index, line in enumerate(raw.splitlines()):
            row = parse_json(line.decode('utf-8'))
            require(index < len(cases) and row['caseId'] == cases[index][0]['caseId']
                    and row['inputSha256'] == digest(cases[index][1]), 'Prediction identity mismatch')
            require({key: row[key] for key in ('valid', 'candidate', 'error')} ==
                    scorer(cases[index][1], row['raw']), 'Changed validity of saved candidate')
            completed.append(row)
    for record, request in cases[len(completed):]:
        began = time.perf_counter()
        generated = generate(request)
        require(type(generated['text']) is str, 'Invalid generation text')
        row = {'caseId': record['caseId'], 'inputSha256': digest(request),
               'elapsedSeconds': time.perf_counter()-began, 'raw': generated,
               **scorer(request, generated)}
        with predictions_file.open('a', encoding='utf-8') as stream:
            stream.write(canonical(row)+'\n')
            stream.flush()
        completed.append(row)
        progress = {'status': 'generating', 'completed': len(completed), 'total': len(cases),
                    'valid': sum(r['valid'] for r in completed), 'engine': identity,
                    'trainingStarted': False, 'tacticalQualityApproved': False}
        temporary = output/'live-status.json.tmp'
        temporary.write_text(canonical(progress)+'\n', encoding='utf-8')
        temporary.replace(output/'live-status.json')
        print(canonical(progress), flush=True)
    summary = {'status': 'generation-complete', 'cases': len(cases),
               'valid': sum(r['valid'] for r in completed), 'engine': identity,
               'tacticalQualityApproved': False, 'trainingStarted': False,
               'totalElapsedSeconds': sum(r['elapsedSeconds'] for r in completed)}
    (output/'generation-summary.json').write_text(canonical(summary)+'\n', encoding='utf-8')
    (output/'live-status.json').write_text(canonical(summary)+'\n', encoding='utf-8')
    return summary


def run_cases(cases, output, identity, generate, scorer=score_generation, system=SYSTEM):
    output.mkdir(parents=True, exist_ok=True)
    lock = output/'.tactical.lock'
    descriptor = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
    os.close(descriptor)
    try:
        return _run_cases(cases, output, identity, generate, scorer, system)
    finally:
        lock.unlink()


def ollama_generator(url, model, max_tokens, system=SYSTEM, examples=None, schema_factory=None, context=8192, thinking=False):
    require(type(thinking) is bool, 'Thinking option must be boolean')
    client = JsonHttpClient(600 if examples or thinking else 300)
    models = client.fetch(url+'api/tags', limit=256*1024)
    match = next((m for m in models['models'] if m['name'] == model), None)
    require(match is not None, 'Model must already be installed; no automatic download')
    identity = {'engine': 'ollama', 'model': model, 'digest': match['digest'],
                'options': {'temperature': 0, 'seed': 42, 'num_ctx': context, 'num_predict': max_tokens},
                'think': thinking, 'format': 'json-schema-v1' if schema_factory else 'json'}
    if examples:
        identity['examplesSha256'] = digest(examples)

    def generate(request):
        reply = client.fetch(url+'api/chat', {'model': model, 'stream': False, 'think': thinking,
                             'format': schema_factory(request) if schema_factory else 'json',
                             'messages': [{'role': 'system', 'content': system}, *(examples or []),
                                          {'role': 'user', 'content': canonical(request)}],
                             'options': identity['options'], 'keep_alive': '10m'}, limit=128*1024)
        return {'text': reply['message']['content'], 'tokens': reply.get('eval_count'),
                'stopReason': reply.get('done_reason'), 'promptTokens': reply.get('prompt_eval_count')}
    return identity, generate


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True)
    selection = parser.add_mutually_exclusive_group(required=True)
    selection.add_argument('--index', type=Path)
    selection.add_argument('--bundle', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--engine', choices=['policy', 'ollama', 'huggingface'], default='policy')
    parser.add_argument('--snapshot', type=Path)
    parser.add_argument('--adapter', type=Path)
    parser.add_argument('--ollama-url', default='http://dotaanalyst-tactical-ollama:11434/')
    parser.add_argument('--model', default='qwen3:8b')
    parser.add_argument('--limit', type=int)
    parser.add_argument('--max-tokens', type=int, default=1024)
    parser.add_argument('--include-support', action='store_true')
    parser.add_argument('--actions', type=Path, help='Opt-in C# action receipt directory (development only)')
    parser.add_argument('--partition', choices=['development','validation-diagnostic'], default='development')
    parser.add_argument('--patches',type=Path,help='Opt-in audited development patch metadata directory')
    args = parser.parse_args(argv)
    cases = load_selection(args.source,args.index,args.bundle,args.include_support,args.partition,args.actions,args.patches)
    if args.limit is not None:
        require(0 < args.limit <= len(cases), 'Invalid limit')
        cases = cases[:args.limit]
    if args.engine == 'ollama':
        require(128 <= args.max_tokens <= 2048, 'Invalid token budget')
        identity, generate = ollama_generator(args.ollama_url, args.model, args.max_tokens)
    elif args.engine == 'huggingface':
        from .huggingface import generator
        require(args.snapshot is not None and 128 <= args.max_tokens <= 2048, 'Snapshot and valid token budget required')
        identity, generate = generator(args.snapshot, args.adapter, args.max_tokens, requests=[q for _, q in cases])
    else:
        identity = {'engine': 'policy-example-v1', 'provisional': True}
        def generate(request):
            return {'text': canonical(EXAMPLE), 'tokens': None, 'stopReason': 'policy'}
    identity['dataPartition'] = args.partition
    print(canonical(run_cases(cases, args.output, identity, generate)))


if __name__ == '__main__':
    main()
