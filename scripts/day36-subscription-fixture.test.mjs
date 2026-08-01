import assert from 'node:assert/strict';
import test from 'node:test';
import {
  day36SubscriptionFixtureIds,
  day36SubscriptionFixtureSql,
} from './day36-subscription-fixture.mjs';

const uuidV4Pattern = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const ids = {
  operatorA: '36000000-0000-4000-8000-000000000001',
  ...day36SubscriptionFixtureIds,
};

test('uses stable UUID-v4 identifiers for the Day 36 subscription fixture', () => {
  assert.match(ids.subscriptionPlan, uuidV4Pattern);
  assert.match(ids.operatorASubscription, uuidV4Pattern);
  assert.notEqual(ids.subscriptionPlan, ids.operatorASubscription);
});

test('seeds an active Shuttle-enabled plan with adequate quotas and usage counters', () => {
  const sql = day36SubscriptionFixtureSql(ids);

  assert.match(sql, /INSERT INTO subscription_plans/);
  assert.match(sql, new RegExp(`'${ids.subscriptionPlan}'`));
  assert.match(sql, /10,10,10,10,10,100/);
  assert.match(sql, /false,true,false,true/);
  assert.match(sql, /INSERT INTO operator_subscriptions/);
  assert.match(
    sql,
    new RegExp(
      `'${ids.operatorASubscription}','${ids.operatorA}','${ids.subscriptionPlan}','ACTIVE'`,
    ),
  );
  assert.match(sql, /3,3,0,2,1,6,date_trunc\('month',now\(\)\)/);
  assert.match(sql, /ON CONFLICT \(id\) DO UPDATE SET/);
});
