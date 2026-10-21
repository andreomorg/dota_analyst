"""JSON schemas for optional constrained local generation; semantic checks remain separate."""
from .curation import DIMENSIONS, KINDS
from .facts import iter_candidate_prose


def obj(properties):
    return {'type':'object','properties':properties,'required':list(properties),'additionalProperties':False}


def text(maximum=400):
    return {'type':'string','minLength':1,'maxLength':maximum}


def array(items, minimum=0, maximum=8):
    return {'type':'array','items':items,'minItems':minimum,'maxItems':maximum}


def candidate_schema(request):
    refs = array({'type':'string','enum':[fact['id'] for fact in request['facts']]},1,8)
    alternative = obj({'id':{'type':'string','enum':['A','B']},'action':text(),
                       'conditions':array(text(),1,4),'evidenceIds':refs,
                       'unknowns':array({'type':'string','enum':request['unknowns']},0,12)})
    return obj({'schemaVersion':{'type':'integer','enum':[1]},
                'disposition':{'type':'string','enum':['supported-review','conditional-review','insufficient']},
                'opportunity':text(),'evidenceIds':refs,'alternatives':array(alternative,0,2),
                'preference':{'type':'string','enum':['A','B','undetermined']},
                'exercise':obj({'steps':array(text(300),1,3),'completionCriterion':text(),'evidenceIds':refs}),
                'limitations':array(text(700),1,8)})


def critique_schema(request,candidate):
    quotes = sorted({value[:400] for value in iter_candidate_prose(candidate) if value})
    failure = obj({'kind':{'type':'string','enum':list(KINDS)},
                   'quote':{'type':'string','enum':quotes or ['No quotable candidate text']},'reason':text(700),
                   'evidenceIds':array({'type':'string','enum':[fact['id'] for fact in request['facts']]},0,8)})
    return obj({'scores':obj({name:{'type':'integer','enum':[0,1,2]} for name in DIMENSIONS}),
                'criticalFailures':array(failure,0,8),
                'verdict':{'type':'string','enum':['reject','revise','provisional-accept']},'reason':text(700)})
