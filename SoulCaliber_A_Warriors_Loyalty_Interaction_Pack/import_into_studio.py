#!/usr/bin/env python3
"""
Import A Warrior's Loyalty into an existing SoulCaliber Studio *master* SQLite DB.

Default: VALIDATE/DRY RUN; nothing written.
Actual import: python import_into_studio.py --db "C:\\path\\to\\master.db" --apply

Requires Phase 12D + the Hide Unmet Dialogue Choices migration (014) in Studio.
This tool does not modify Godot runtime/savegame DBs or create backups.
Do not run it against a published read-only content snapshot.
"""
from __future__ import annotations
import argparse
import json
import sqlite3
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
PACK = HERE / 'nobunogi_warriors_loyalty.json'
REQUIRED = {
    'Interactions': {'Id','Key','Title','HostCharacterId','StartNodeKey'},
    'Nodes': {'Id','InteractionId','Key','Round','SortOrder','HostIntroduction'},
    'Options': {'Id','NodeId','Key','Label','SortOrder','BaseSuccessChance',
                'SuccessNextNodeKey','FailureNextNodeKey','DefaultPlayerLine',
                'DefaultHostSuccessLine','DefaultHostFailureLine','SuccessTargetReaction',
                'FailureTargetReaction','UnmetRequirementBehaviour'},
    'Requirements': {'OptionId','Direction','Attribute','Minimum','Maximum'},
    'Effects': {'OptionId','Outcome','Direction','Attribute','Delta'},
    'SkillChecks': {'OptionId','ActorSkill','DefenderSkill','CheckMode','ActorFactor','DefenderFactor'},
    'OptionCheckProfiles': {'OptionId','ProfileKey'},
    'OptionStatusRequirements': {'OptionId','Direction','StatusKey'},
    'OptionStatusEffects': {'OptionId','Outcome','Direction','StatusKey'},
    'CheckProfiles': {'Key'},
    'CharacterSkills': {'Key','IsActive'},
    'RelationshipAttributes': {'Key','IsActive','MinValue','MaxValue'},
    'RelationshipStatuses': {'Key','IsActive'},
    'ReactionTypes': {'Key'},
}

def validate_pack(pack):
    nodes=pack['nodes']
    node_keys=[n['key'] for n in nodes]
    if len(set(node_keys)) != len(node_keys):raise ValueError('Duplicate node keys in JSON')
    if pack['start_node'] not in node_keys:raise ValueError('Starting node missing in JSON')
    choices=[]
    for n in nodes:
        if not n['options']:raise ValueError(f'Node {n["key"]} has no choices')
        for o in n['options']:
            choices.append(o)
            for field in ('success_next','failure_next'):
                if o[field] is not None and o[field] not in node_keys:
                    raise ValueError(f'Unknown next node in {o["key"]}: {o[field]}')
    if len({x['key'] for x in choices})!=len(choices):raise ValueError('Duplicate choice keys in JSON')
    return choices

def fetch_active(db,table,extra=''):
    return {str(row[0]).upper() for row in db.execute(f'SELECT Key FROM {table} {extra}')}

def check_db(db,pack):
    # Fail early with a clear message if the user's database has a different schema.
    for table,expected in REQUIRED.items():
        columns={r[1] for r in db.execute('PRAGMA table_info("'+table+'")')}
        if not expected.issubset(columns):
            raise ValueError(f"Table {table} is missing or has an unsupported schema; missing columns: {sorted(expected-columns)}. "
                             'Open the master database in the latest Studio before importing.')
    if db.execute('SELECT 1 FROM Interactions WHERE Key=? COLLATE NOCASE',
                  (pack['interaction_key'],)).fetchone():
        raise ValueError(f"Interaction '{pack['interaction_key']}' already exists. Refusing to overwrite.")

    active_attrs={r[0].upper():(r[1],r[2]) for r in db.execute(
        'SELECT Key,MinValue,MaxValue FROM RelationshipAttributes WHERE IsActive=1')}
    active_status=fetch_active(db,'RelationshipStatuses','WHERE IsActive=1')
    active_skills=fetch_active(db,'CharacterSkills','WHERE IsActive=1')
    reactions=fetch_active(db,'ReactionTypes')
    profiles=fetch_active(db,'CheckProfiles')

    for n in pack['nodes']:
        for o in n['options']:
            name=o['key']
            if o['success_reaction'].upper() not in reactions or o['failure_reaction'].upper() not in reactions:
                raise ValueError(f'{name}: missing a reaction type; check the Studio catalogue.')
            if o['check_profile'].upper() not in profiles:
                raise ValueError(f'{name}: check profile {o["check_profile"]} is missing.')
            if o['skill_check'] and any(o['skill_check'][part].upper() not in active_skills
                                        for part in ('actor','defender')):
                raise ValueError(f'{name}: one of its skills is missing/inactive.')
            for r in o['status_requirements']+o['status_effects']:
                if r['status'].upper() not in active_status:
                    raise ValueError(f'{name}: missing or inactive relationship status {r["status"]}.')
            for r in o['numeric_requirements']:
                if r['attribute'].upper() not in active_attrs:
                    raise ValueError(f'{name}: unknown/inactive attribute {r["attribute"]}.')
                lo,hi=active_attrs[r['attribute'].upper()]
                for field in ('min','max'):
                    if r[field] is not None and not lo<=r[field]<=hi:
                        raise ValueError(f'{name}: {field} requirement {r[field]} outside {lo}..{hi}.')
            for e in o['numeric_effects']:
                if e['attribute'].upper() not in active_attrs:
                    raise ValueError(f'{name}: unknown/inactive effect attribute {e["attribute"]}.')
    return True

def print_summary(pack,db=None):
    nodes=pack['nodes']
    options=[o for n in nodes for o in n['options']]
    print('Interaction:',pack['title'],f"[{pack['interaction_key']}]")
    print('Host/visitor intent:',pack['intended_host'],'/',pack['intended_visitor'])
    print('Content:',len(nodes),'nodes,',len(options),'choices,',
          sum(o['unmet_visibility']=='HIDE' for o in options),'hidden-on-unmet choices.')
    if db is not None:
        tables={r[0] for r in db.execute("SELECT name FROM sqlite_master WHERE type='table'")}
        if 'Characters' in tables:
            known={r[0].upper() for r in db.execute('SELECT Id FROM Characters')}
            for char in ('NOBUNOGI','LAW'):
                if char not in known:
                    print(f"NOTE: character ID '{char}' isn't in the roster. The interaction uses generic text; "
                          "select the correct character IDs when launching it.")

def insert(db,pack):
    # One transaction for the entire authoring operation; cannot partially import.
    db.execute('BEGIN IMMEDIATE')
    try:
        cur=db.execute('INSERT INTO Interactions(Key,Title,HostCharacterId,StartNodeKey) VALUES(?,?,?,?)',
                       (pack['interaction_key'],pack['title'],'',pack['start_node']))
        interaction_id=cur.lastrowid
        node_ids={}
        for sort,n in enumerate(pack['nodes']):
            cur=db.execute('INSERT INTO Nodes(InteractionId,Key,Round,SortOrder,HostIntroduction) VALUES(?,?,?,?,?)',
                           (interaction_id,n['key'],n['round'],sort,n['host_introduction']))
            node_ids[n['key']]=cur.lastrowid
        for n in pack['nodes']:
            for sort,o in enumerate(n['options']):
                params=(node_ids[n['key']],o['key'],o['label'],sort,o['base_chance'],
                        o['success_next'],o['failure_next'],o['player_line'],
                        o['host_success_line'],o['host_failure_line'],o['success_reaction'],
                        o['failure_reaction'],o['unmet_visibility'])
                cur=db.execute('''INSERT INTO Options(
                         NodeId,Key,Label,SortOrder,BaseSuccessChance,SuccessNextNodeKey,FailureNextNodeKey,
                         DefaultPlayerLine,DefaultHostSuccessLine,DefaultHostFailureLine,
                         SuccessTargetReaction,FailureTargetReaction,UnmetRequirementBehaviour)
                         VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?)''',params)
                option_id=cur.lastrowid
                db.execute('INSERT INTO OptionCheckProfiles(OptionId,ProfileKey) VALUES(?,?)',
                           (option_id,o['check_profile']))
                for r in o['numeric_requirements']:
                    db.execute('INSERT INTO Requirements(OptionId,Direction,Attribute,Minimum,Maximum) VALUES(?,?,?,?,?)',
                               (option_id,r['direction'],r['attribute'],r['min'],r['max']))
                for r in o['status_requirements']:
                    db.execute('INSERT INTO OptionStatusRequirements(OptionId,Direction,StatusKey) VALUES(?,?,?)',
                               (option_id,r['direction'],r['status']))
                for e in o['numeric_effects']:
                    db.execute('INSERT INTO Effects(OptionId,Outcome,Direction,Attribute,Delta) VALUES(?,?,?,?,?)',
                               (option_id,e['outcome'],e['direction'],e['attribute'],e['delta']))
                for e in o['status_effects']:
                    db.execute('INSERT INTO OptionStatusEffects(OptionId,Outcome,Direction,StatusKey) VALUES(?,?,?,?)',
                               (option_id,e['outcome'],e['direction'],e['status']))
                if o['skill_check'] is not None:
                    sk=o['skill_check']
                    db.execute('''INSERT INTO SkillChecks
                              (OptionId,ActorSkill,DefenderSkill,CheckMode,ActorFactor,DefenderFactor)
                              VALUES(?,?,?,?,?,?)''',
                              (option_id,sk['actor'],sk['defender'],sk['profile'],sk['actor_factor'],sk['defender_factor']))
        invalid=db.execute('PRAGMA foreign_key_check').fetchall()
        if invalid:raise ValueError(f'Foreign-key errors after import: {invalid[:5]}')
        db.commit()
        return interaction_id
    except Exception:
        db.rollback()
        raise

def main(argv=None):
    p=argparse.ArgumentParser(description=__doc__,formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('--db',required=True,help='Existing SoulCaliber Studio master content DB (.db).')
    p.add_argument('--apply',action='store_true',help='Write the complete interaction atomically (default is read-only dry run).')
    args=p.parse_args(argv)
    pack=json.loads(PACK.read_text(encoding='utf8'))
    validate_pack(pack)
    path=Path(args.db).expanduser().resolve()
    if not path.is_file():p.error('The database file does not exist: '+str(path))
    if path.name.lower().endswith('savegame.db'):
        p.error('Do not import into a runtime savegame database.')
    uri=path.as_uri()+'?mode='+('rw' if args.apply else 'ro')
    with sqlite3.connect(uri,uri=True) as db:
        db.execute('PRAGMA foreign_keys=ON')
        check_db(db,pack)
        print_summary(pack,db)
        if args.apply:
            print('Importing into:',path)
            ident=insert(db,pack)
            print('SUCCESS. Imported interaction ID:',ident)
            print('Open it in Studio, review dialogue, then publish updated content.')
        else:
            print('DRY RUN PASSED — no changes made.')
            print('To import, make a manual backup/commit if desired, then rerun with --apply.')

if __name__=='__main__':
    try:main()
    except (sqlite3.Error,ValueError,OSError) as ex:
        print('IMPORT NOT APPLIED:',ex,file=sys.stderr)
        sys.exit(1)
