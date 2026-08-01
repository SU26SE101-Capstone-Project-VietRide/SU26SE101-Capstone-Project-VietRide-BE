export const day36SubscriptionFixtureIds = Object.freeze({
  subscriptionPlan: '36000000-0000-4000-8000-000000000141',
  operatorASubscription: '36000000-0000-4000-8000-000000000142',
});

export function day36SubscriptionFixtureSql(ids) {
  return `
    INSERT INTO subscription_plans (
      id,name,description,price_per_month,price_per_year,
      max_vehicles,max_drivers,max_assistants,max_operator_users,max_routes,max_trips_per_month,
      enable_parcel,enable_shuttle,enable_rag,is_active
    )
    VALUES (
      '${ids.subscriptionPlan}','Day 36 Shuttle E2E','Deterministic Shuttle-enabled E2E fixture',0,0,
      10,10,10,10,10,100,
      false,true,false,true
    )
    ON CONFLICT (id) DO UPDATE SET
      name=EXCLUDED.name,
      description=EXCLUDED.description,
      price_per_month=EXCLUDED.price_per_month,
      price_per_year=EXCLUDED.price_per_year,
      max_vehicles=EXCLUDED.max_vehicles,
      max_drivers=EXCLUDED.max_drivers,
      max_assistants=EXCLUDED.max_assistants,
      max_operator_users=EXCLUDED.max_operator_users,
      max_routes=EXCLUDED.max_routes,
      max_trips_per_month=EXCLUDED.max_trips_per_month,
      enable_parcel=EXCLUDED.enable_parcel,
      enable_shuttle=EXCLUDED.enable_shuttle,
      enable_rag=EXCLUDED.enable_rag,
      is_active=true;
    INSERT INTO operator_subscriptions (
      id,operator_id,active_plan_id,status,started_at,expires_at,
      current_vehicles,current_drivers,current_assistants,current_operator_users,
      current_routes,current_trips_this_month,last_reset_at
    )
    VALUES (
      '${ids.operatorASubscription}','${ids.operatorA}','${ids.subscriptionPlan}','ACTIVE',
      now()-interval '1 day',now()+interval '30 days',
      3,3,0,2,1,6,date_trunc('month',now())
    )
    ON CONFLICT (id) DO UPDATE SET
      operator_id=EXCLUDED.operator_id,
      active_plan_id=EXCLUDED.active_plan_id,
      status='ACTIVE',
      started_at=EXCLUDED.started_at,
      expires_at=EXCLUDED.expires_at,
      payment_method=NULL,
      billing_period=NULL,
      current_vehicles=EXCLUDED.current_vehicles,
      current_drivers=EXCLUDED.current_drivers,
      current_assistants=EXCLUDED.current_assistants,
      current_operator_users=EXCLUDED.current_operator_users,
      current_routes=EXCLUDED.current_routes,
      current_trips_this_month=EXCLUDED.current_trips_this_month,
      last_reset_at=EXCLUDED.last_reset_at;
  `;
}
