"""Assemble a new experimental directory from accepted curation artifacts, without loading ML."""
import argparse
import copy
from pathlib import Path

from ..core.json import canonical, parse_json
from ..core.validation import require
from ..hashing import file_hash
from ..datasets import audit_experiment
from ..experiments.preparation import load_config
from .pipeline import SYSTEM


def normalize_metadata(meta, audit):
    value = copy.deepcopy(meta)
    provenance = value['provenance']
    scene = f"{provenance['matchId']}/{provenance['heroId']}/{provenance['sceneId']}"
    expected = {'matchId':provenance['matchId'],'heroId':provenance['heroId'],'episodeId':provenance['sceneId'],
                'sourceHash':provenance['sourceMatchHash'],'sourceSceneId':scene,
                'exampleId':scene+'/tactical-review-v1','provisionalAccepted':True,
                'sourcePredictionsSha256':audit['sourcePredictionsSha256'],
                'criticIdentitySha256':audit['criticIdentitySha256']}
    for key, item in expected.items():
        require(key not in value or value[key] == item, 'Curated metadata identity differs: '+key)
        value[key] = item
    return value


def assemble(train, validation, config, manifest, output, minimum_train=3000, minimum_validation=100):
    train, validation, output = Path(train).resolve(), Path(validation).resolve(), Path(output).resolve()
    require(train != validation and not output.exists() and not output.is_relative_to(train)
            and not output.is_relative_to(validation), 'Experiment must have a separate new directory')
    require(minimum_train > 0 and minimum_validation > 0, 'Positive dataset minimums required')
    cfg = parse_json(Path(config).read_text(encoding='utf-8-sig'))
    require(cfg.get('profile') == 'tactical-review-v1', 'Explicit tactical training profile required')
    prepared, fingerprints = {}, {}
    for stem, directory, minimum in (('development',train,minimum_train),('validation',validation,minimum_validation)):
        veto = directory/'assistant-review-veto.json'
        if veto.exists():
            require(parse_json(veto.read_text()).get('allowedForTraining') is not False, 'Curated source has a content review veto')
        names = ('provisional-sft.jsonl','metadata.json','curation-audit.json')
        fingerprints[stem] = {name:file_hash(directory/name) for name in names}
        audit = parse_json((directory/'curation-audit.json').read_text())
        require(audit.get('profile') == 'tactical-review-v1' and audit.get('experimentalDataReady') is True
                and audit.get('humanReviewed') is False and audit.get('tacticalQualityApproved') is False,
                'Curated source has not passed its provisional gate')
        metadata = parse_json((directory/'metadata.json').read_text())
        require(type(metadata) is list and len(metadata) >= minimum and len(metadata) == audit['provisionalAccepted'],
                'Curated source does not meet dataset minimum')
        prepared[stem] = (directory,[normalize_metadata(row,audit) for row in metadata])
    output.mkdir()
    cfg.update(samples=len(prepared['development'][1]),validationSamples=len(prepared['validation'][1]))
    (output/'config.json').write_text(canonical(cfg)+'\n',encoding='utf-8')
    load_config(output)
    for stem,(directory,metadata) in prepared.items():
        (output/(stem+'.jsonl')).write_bytes((directory/'provisional-sft.jsonl').read_bytes())
        (output/(stem+'-metadata.json')).write_text(canonical(metadata)+'\n',encoding='utf-8')
    (output/'instructions.txt').write_text(SYSTEM,encoding='utf-8')
    (output/'experimental-authorization.json').write_text(canonical({
        'scope':'User-requested separate tactical learning experiment; provisional labels, no promotion'})+'\n',encoding='utf-8')
    joint = audit_experiment(output,manifest,'tactical-review-v1')
    require(joint['inputIsolationPassed'], 'Training/validation duplicate inputs or shared matches')
    for stem,(directory,_) in prepared.items():
        require(fingerprints[stem] == {name:file_hash(directory/name) for name in fingerprints[stem]},
                'Curated source changed during assembly')
    report = {'profile':'tactical-review-v1','samples':cfg['samples'],'validationSamples':cfg['validationSamples'],
              'inputIsolationPassed':True,'curationSources':fingerprints,'manifestSha256':file_hash(manifest),
              'trainingStarted':False,'tacticalQualityApproved':False,'humanReviewed':False,
              'gpuBudgetVerified':False,'tokenBudgetVerified':False,
              'validationPurpose':'development-diagnostic; not a new blind test'}
    names = ('config.json','instructions.txt','experimental-authorization.json','development.jsonl',
             'development-metadata.json','validation.jsonl','validation-metadata.json')
    report['preparedFiles'] = {name:file_hash(output/name) for name in names}
    with (output/'assembly-audit.json').open('x',encoding='utf-8') as stream:
        stream.write(canonical(report)+'\n')
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('train-curated','validation-curated','config','manifest','output'):
        parser.add_argument('--'+name,type=Path,required=True)
    parser.add_argument('--minimum-train',type=int,default=3000)
    parser.add_argument('--minimum-validation',type=int,default=100)
    args = parser.parse_args(argv)
    print(canonical(assemble(args.train_curated,args.validation_curated,args.config,args.manifest,args.output,
                             args.minimum_train,args.minimum_validation)))


if __name__ == '__main__':
    main()
