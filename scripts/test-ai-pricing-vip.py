#!/usr/bin/env python3
"""Run the VIP pricing HTTP smoke test with isolated Docker fixtures.

Run from the repository with python3 scripts/test-ai-pricing-vip.py.
Requires .NET 10 plus locally available mysql:8.4, redis:7.4-alpine and
jokester-admin:local (for its .NET runtime and curl). Fixtures are printed
and kept for inspection. No production configuration or provider is used.
"""

import argparse
from concurrent.futures import ThreadPoolExecutor
import json
from pathlib import Path
import re
import secrets
import subprocess
import tempfile
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]
PREFIX = f'vip-pricing-{uuid.uuid4().hex[:8]}'
MYSQL, REDIS, API = (f'{PREFIX}-{name}' for name in ('mysql', 'redis', 'api'))
DB = 'vip_smoke'
PASSWORD = secrets.token_urlsafe(24) + '1a!'
IMAGE = 'jokester-admin:local'


def run(command, data=None):
    result = subprocess.run(command, input=data, capture_output=True, text=True, cwd=ROOT)
    if result.returncode:
        # Arguments and input can contain ephemeral credentials.
        raise RuntimeError(f'{command[0]} exited {result.returncode}: {result.stderr[-800:]}')
    return result.stdout.strip()


def sql(statement, use_db=True):
    args = ['docker', 'exec', '-i', MYSQL, 'mysql', '-h127.0.0.1', '--batch', '--skip-column-names', '--default-character-set=utf8mb4']
    return run(args + ([DB] if use_db else []), statement)


def until(operation, seconds=60):
    deadline = time.monotonic() + seconds
    last = None
    while time.monotonic() < deadline:
        try:
            answer = operation()
            if answer:
                return answer
        except (RuntimeError, ValueError) as error:
            last = error
        time.sleep(.5)
    raise RuntimeError(f'fixture readiness timed out: {last}')


def api(path, token=None, body=None, method=None, expected=200, code=None):
    cmd = ['docker', 'exec', '-i', API, 'curl', '-sS', '-w', '\n%{http_code}', '--max-time', '15',
           '-X', method or ('POST' if body is not None else 'GET'), 'http://127.0.0.1:8080' + path]
    if token:
        cmd += ['-H', 'Authorization: Bearer ' + token]
    if body is not None:
        cmd += ['-H', 'Content-Type: application/json', '--data-binary', '@-']
    for attempt in range(2):
        response, _, status = run(cmd, json.dumps(body) if body is not None else None).rpartition('\n')
        envelope = json.loads(response) if response else {}
        if status == '429' and envelope.get('code') == 'RATE_LIMITED' and not attempt:
            # Production middleware permits two generation requests per user per minute.
            time.sleep(60)
            continue
        break
    assert int(status) == expected, (path, status, envelope.get('code'), envelope.get('message'))
    if code:
        assert envelope['code'] == code, (path, envelope['code'], code)
        return envelope.get('details')
    return envelope.get('data')


def login(name):
    data = api('/api/auth/login', body={'userName': name, 'password': PASSWORD})
    return data.get('accessToken') or data['token']


def check_guide(model, ids, tokens):
    path = '/api/ai/model-pricing-events/vip-guide/' + model
    def logged(name, status):
        return int(sql(f"SELECT COUNT(*) FROM sys_operation_log WHERE user_id={ids[name]} AND request_url='{path}' "
                       f"AND request_method='POST' AND JSON_EXTRACT(response_data,'$.statusCode')={status}"))
    before = {name: logged(name, status) for name, status in [('regular', 204), ('vipuser', 409)]}
    prior_vip_clicks = logged('vipuser', 204)
    for name, status in [('regular', 204), ('vipuser', 409)]:
        api(path, tokens[name], method='POST', expected=status)
        until(lambda: logged(name, status) == before[name] + 1)
    assert logged('vipuser', 204) == prior_vip_clicks
    print('PASS locked user guide click logged at 204, active VIP rejected and logged at 409', flush=True)


def quota(user_id, unit):
    keys = run(['docker', 'exec', REDIS, 'redis-cli', '--raw', 'KEYS', f'*:daily:*:{unit}:{user_id}']).splitlines()
    return sum(int(run(['docker', 'exec', REDIS, 'redis-cli', '--raw', 'GET', key])) for key in keys)


def verify_upgrade(baseline_schema):
    """Compare the baseline upgrade to a fresh install inside this smoke run's isolated MySQL."""
    upgrade = 'vip_upgrade_' + uuid.uuid4().hex[:8]
    sql(baseline_schema.read_text().replace('`jokester.admin`', f'`{upgrade}`'), False)

    def upgraded(statement):
        return sql(f'USE `{upgrade}`;\n' + statement, False)

    upgraded("""INSERT INTO ai_image_point_price
        (model_code,resolution_code,quality_code,points,price_amount,status,is_deleted) VALUES
        ('edge','1k','',3,0,0,0),('deleted','4k','',12,0,1,1),('round','1k','',12,0,1,0);""")
    prices = 'SELECT id,points FROM ai_image_point_price ORDER BY id'
    snapshot = upgraded(prices)
    migration = (ROOT / 'docs/migrations/20261124-add-ai-pricing-admin-and-vip-models.sql').read_text()
    upgraded(migration)
    assert upgraded(prices) == snapshot, 'migration changed a standard price'
    check = """SELECT COUNT(*) FROM ai_image_point_price p WHERE NOT p.is_deleted AND
        (NOT (vip_points <=> NULLIF(CEIL(points * 0.80), points)) OR
        (SELECT COUNT(*) FROM ai_image_pricing_audit a WHERE a.action='system_init' AND a.operator_user_id=0
          AND a.target_type='price' AND a.target_id=CAST(p.id AS CHAR) COLLATE utf8mb4_unicode_ci) <> 1);"""
    for query in (sql, upgraded):
        assert query(check) == '0', 'incorrect initial VIP price or missing per-row system audit'
        assert query('SELECT revision,pricing_revision,default_vip_discount_percent FROM ai_image_pricing_setting') == '0\t1\t80'
    for table in ('ai_image_point_price', 'ai_image_task', 'ai_image_pricing_setting', 'ai_image_model_policy', 'ai_image_pricing_audit'):
        ddl = 'SHOW CREATE TABLE ' + table
        assert re.sub(r'AUTO_INCREMENT=\d+ ', '', sql(ddl)) == re.sub(r'AUTO_INCREMENT=\d+ ', '', upgraded(ddl)), table
    upgraded("""UPDATE ai_image_point_price SET vip_price_mode='manual',vip_points=2,revision=3 WHERE model_code='round';
        UPDATE ai_image_pricing_setting SET revision=1,pricing_revision=3,default_vip_discount_percent=70;""")
    audits = upgraded('SELECT COUNT(*) FROM ai_image_pricing_audit')
    upgraded(migration)
    assert upgraded('SELECT COUNT(*) FROM ai_image_pricing_audit') == audits
    assert upgraded("SELECT vip_price_mode,vip_points,revision FROM ai_image_point_price WHERE model_code='round'") == 'manual\t2\t3'
    assert upgraded('SELECT revision,pricing_revision,default_vip_discount_percent FROM ai_image_pricing_setting') == '1\t3\t70'
    assert upgraded("SELECT vip_points,updated_by FROM ai_image_point_price WHERE model_code='deleted'") == 'NULL\tNULL'
    print('PASS standard prices, VIP rounding, per-price audit, five table definitions, migration rerun:', upgrade, flush=True)


def prepare(baseline_schema=None):
    publish = tempfile.mkdtemp(prefix=PREFIX + '-publish-')
    print('Fixtures:', MYSQL, REDIS, API, DB, publish, flush=True)
    subprocess.run(['dotnet', 'publish', 'jokester.admin/jokester.admin.csproj', '--no-restore', '-p:UseAppHost=false', '-o', publish, '-v:q'], cwd=ROOT, check=True)
    run(['docker', 'run', '--pull=never', '-d', '--name', MYSQL, '--network=none', '--tmpfs', '/var/lib/mysql',
         '-e', 'MYSQL_ALLOW_EMPTY_PASSWORD=yes', 'mysql:8.4'])
    until(lambda: sql('SELECT 1', False) == '1')
    schema = (ROOT / 'jokester.admin.sql').read_text().replace('`jokester.admin`', f'`{DB}`')
    sql(schema, False)
    if baseline_schema is not None:
        verify_upgrade(baseline_schema)
    local = ['docker', 'run', '--pull=never', '--network', 'container:' + MYSQL]
    run(local + ['-d', '--name', REDIS, '--tmpfs', '/data', 'redis:7.4-alpine', 'redis-server', '--save', '', '--appendonly', 'no'])
    # The network namespace only has loopback. A closed local port exercises real worker refunds.
    sql("UPDATE ai_image_model_config SET api_key='fixture',base_url='http://127.0.0.1:7901'")
    config = {'ASPNETCORE_ENVIRONMENT': 'Development', 'ASPNETCORE_URLS': 'http://127.0.0.1:8080',
              'AllowedHosts': 'localhost;127.0.0.1', 'Security__AllowedOrigins__0': 'http://localhost',
              'Jwt__SecretKey': secrets.token_urlsafe(48), 'Jwt__Issuer': 'vip-smoke', 'Jwt__Audience': 'vip-smoke',
              'BootstrapAdmin__UserName': 'admin', 'BootstrapAdmin__Password': PASSWORD,
              'Database__ConnectionString': f'Server=127.0.0.1;Database={DB};User ID=root;Allow User Variables=True;SslMode=None;AllowPublicKeyRetrieval=True;',
              'Redis__ConnectionString': '127.0.0.1:6379,abortConnect=false', 'Redis__InstanceName': 'vip-smoke',
              'Mail__FromAddress': 'fixture@example.invalid', 'Mail__Host': '127.0.0.1', 'PROMPT_LIBRARY_ENABLED': 'false'}
    options = [value for k, v in config.items() for value in ('-e', k + '=' + v)]
    runtime = local + options + ['-v', publish + ':/test:ro', '--entrypoint', 'dotnet']
    run(runtime + ['--rm', IMAGE, '/test/jokester.admin.dll', '--seed-admin'])
    run(runtime + ['-d', '--name', API, IMAGE, '/test/jokester.admin.dll'])
    until(lambda: api('/api/sites/site_code'))


def smoke():
    admin = login('admin')
    site = int(sql("SELECT id FROM sys_site WHERE site_code='ai_image'"))
    role = int(sql("SELECT id FROM sys_role WHERE role_code='ai_operator'"))
    tokens = {'admin': admin}
    ids = {'admin': int(sql("SELECT id FROM sys_user WHERE user_name='admin'"))}
    for name in ('regular', 'vipuser', 'vipadmin'):
        ids[name] = api('/api/users', admin, {'userName': name, 'nickName': name, 'password': PASSWORD, 'roleIds': [role], 'siteIds': [site]})['id']
        if name == 'vipadmin':
            sql(f'UPDATE sys_user SET is_super_admin=1 WHERE id={ids[name]}')
        if name in ('vipuser', 'vipadmin'):
            sql(f"INSERT INTO sys_user_membership_entitlement(user_id,tier_code,source,business_key,starts_at,expires_at,created_at) VALUES ({ids[name]},'monthly_vip','fixture','{name}',NOW()-INTERVAL 1 DAY,NOW()+INTERVAL 1 DAY,NOW())")
        tokens[name] = login(name)
    sql('UPDATE sys_user SET point_balance=500')
    for uid in ids.values():
        sql(f"INSERT INTO sys_user_point_bucket(user_id,source,business_key,granted_points,remaining_points,created_at) VALUES ({uid},'recharge','fixture',500,500,NOW())")
    pricing = '/api/ai/model-pricing'
    api(pricing, tokens['regular'], expected=403, code='RESOURCE_FORBIDDEN')
    model, nano = 'gpt-image-2.6-sunburst', 'nano-banana-2'
    overview = api(pricing, admin)
    for target in (model, nano):
        group = next(m for m in overview['models'] if m['modelCode'] == target)
        api(pricing + '/models/' + target + '/access', admin, {'accessLevel': 'vip_only', 'revision': group['revision'], 'reason': '隔离验收'}, 'PUT')
    check_guide(model, ids, tokens)
    listing = next(m for m in overview['models'] if m['modelCode'] == model)
    price = next(p for p in listing['prices'] if p['resolutionCode'] == '1k' and p['qualityCode'] == 'low')
    points, vip = price['points'], price['vipPoints']
    assert vip < points
    def request(target):
        return {'idempotencyKey': uuid.uuid4().hex, 'siteId': site, 'prompt': 'A watercolor of a tree', 'modelCode': target,
                'modelName': target, 'resolutionCode': '1k', 'qualityCode': 'low', 'aspectRatioCode': '1:1', 'imageCount': 1}
    images = '/api/ai/images'
    for suffix, target in [('', model), ('/generate', model), ('/nanoBananaImage', nano), ('/nanoBananaImage/generate', nano)]:
        api(images + suffix, tokens['regular'], request(target), expected=403, code='VIP_REQUIRED')
    def count(table, condition):
        return int(sql(f'SELECT COUNT(*) FROM {table} WHERE {condition}'))
    assert count('ai_image_task', '1=1') == 0
    assert count('ai_image_request_idempotency', '1=1') == 0
    assert count('sys_user_point_detail', "business_key LIKE 'image:%:reserve'") == 0
    assert quota(ids['regular'], 'points') == quota(ids['regular'], 'images') == 0
    print('PASS ordinary account denied on four creation routes, no task or Redis quota', flush=True)
    for name, available in [('regular', False), ('admin', True), ('vipuser', True), ('vipadmin', True)]:
        models = api(images + '/models', tokens[name])
        target = next(m for m in models if m.get('modelCode', m.get('code')) == model)
        assert target['access']['available'] == available, (name, target['access'])
    # Capture the actual quote seen by each user, then raise the price via the real admin API.
    def quoted(name):
        prices = api(images + '/pricing-options', tokens[name])
        return next(p['effectivePoints'] for p in prices if p['modelCode'] == model and
                    p['resolutionCode'] == '1k' and p['qualityCode'] == 'low')

    identities = [('vipuser', 'vip', 'vip'), ('admin', 'super_admin', 'standard'), ('vipadmin', 'vip', 'vip')]
    old_quotes = {name: quoted(name) for name, _, _ in identities}
    updated = api(pricing + '/prices/batch', admin, {'reason': '隔离调价验收', 'items': [
        {'id': price['id'], 'revision': price['revision'], 'points': points + 2, 'vipPriceMode': 'follow'}]}, 'PUT')['prices'][0]
    points, vip = updated['points'], updated['vipPoints']
    leases_key = 'vip-smoke:security:ai:provider:leases'
    lease_names = [f'smoke-{i}' for i in range(4)]
    expires = str(int(time.time() * 1000) + 300000)
    run(['docker', 'exec', REDIS, 'redis-cli', 'ZADD', leases_key, *[v for lease in lease_names for v in (expires, lease)]])
    def reserve(identity):
        name, basis, tier = identity
        expected_cost = vip if tier == 'vip' else points
        assert old_quotes[name] < expected_cost == quoted(name)
        uid, token = ids[name], tokens[name]
        body = request(model)
        body['expectedUnitPoints'] = old_quotes[name]
        detail = api(images, token, body, expected=409, code='PRICE_CHANGED')
        assert detail['effectivePoints'] == expected_cost and detail['pricingTier'] == tier
        assert count('ai_image_task', f'user_id={uid}') == 0
        assert count('ai_image_request_idempotency', f'user_id={uid}') == 0
        assert count('sys_user_point_detail', f"user_id={uid} AND business_key LIKE 'image:%:reserve'") == 0
        assert quota(uid, 'points') == quota(uid, 'images') == 0
        body['expectedUnitPoints'] = expected_cost
        result = api(images, token, body)
        task = result['ids'][0]
        assert result['pricingTier'] == tier and result['unitPointCost'] == expected_cost
        persisted = sql(f'SELECT pricing_tier,point_cost,list_unit_point_cost,access_basis FROM ai_image_task WHERE id={task}')
        assert persisted.split('\t') == [tier, str(expected_cost), str(points), basis], (name, persisted)
        assert quota(uid, 'points') == expected_cost and quota(uid, 'images') == 1
        assert int(sql(f'SELECT point_balance FROM sys_user WHERE id={uid}')) == 500 - expected_cost
        # A revoked member can still replay their own committed request.
        if name == 'vipuser':
            sql(f"UPDATE sys_user_membership_entitlement SET status='revoked',revoked_at=NOW() WHERE user_id={uid}")
        body['expectedUnitPoints'] = 1
        assert api(images, token, body)['ids'] == result['ids']
        return uid, task, name, expected_cost

    # Hold all provider slots in this isolated Redis while inspecting the live reservations.
    try:
        with ThreadPoolExecutor(max_workers=3) as pool:
            pending = list(pool.map(reserve, identities))
    finally:
        run(['docker', 'exec', REDIS, 'redis-cli', 'ZREM', leases_key, *lease_names])
    for uid, task, name, expected_cost in pending:
        def record(key):
            return sql(f"SELECT change_points FROM sys_user_point_detail WHERE business_key='image:{task}:{key}'")
        assert int(record('reserve')) == -expected_cost
        until(lambda: record('refund') == str(expected_cost))
        assert count('ai_image_task', f'user_id={uid}') == 1
        assert int(sql(f'SELECT point_balance FROM sys_user WHERE id={uid}')) == 500
        until(lambda: quota(uid, 'points') == quota(uid, 'images') == 0)
        print(f'PASS {name}: one reserve at {expected_cost}, exact refund, replay returned original task, quotas settled', flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline-schema', type=Path, help='also verify upgrade from this pre-pricing SQL schema')
    args = parser.parse_args()
    prepare(args.baseline_schema)
    smoke()
