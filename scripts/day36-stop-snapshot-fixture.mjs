export function day36StopSnapshotFixtureSql(ids) {
  return `
    INSERT INTO stops (id,operator_id,name,latitude,longitude,address,is_active)
    VALUES ('${ids.stop}','${ids.operatorA}','Day 36 Along-route Stop',10.8500000,106.7600000,'Day 36 fixture stop',true)
    ON CONFLICT (id) DO UPDATE SET
      operator_id=EXCLUDED.operator_id,
      name=EXCLUDED.name,
      latitude=EXCLUDED.latitude,
      longitude=EXCLUDED.longitude,
      address=EXCLUDED.address,
      is_active=true,
      deleted_at=NULL;
    INSERT INTO trip_stops (trip_id,stop_id,order_index,estimated_arrival_time,status,allow_pickup,allow_dropoff,distance_from_origin_km)
    VALUES ('${ids.mainTrip}','${ids.stop}',1,now()+interval '7 hours','PENDING',true,true,50.00)
    ON CONFLICT (trip_id,stop_id) DO UPDATE SET
      order_index=EXCLUDED.order_index,
      estimated_arrival_time=EXCLUDED.estimated_arrival_time,
      status='PENDING',
      allow_pickup=true,
      allow_dropoff=true,
      distance_from_origin_km=EXCLUDED.distance_from_origin_km;
  `;
}
