# Day-30 Sprint-4 demo evidence

AUTO_FROM_SCHEDULE generated-Trip proof completed before fixture adjustment.
Fixture-only time advance changed only the generated Trip departure timestamp.
Trip state evidence: SCHEDULED -> BOARDING -> IN_PROGRESS -> COMPLETED.
Parcel state evidence: PENDING -> LOADED -> IN_TRANSIT -> UNLOADED.
Required Outbox evidence: trip.trip.boarding_started, trip.trip.started, parcel.parcel.loaded, trip.stop.arrived, parcel.parcel.unloaded, trip.trip.completed.
completion replay verified with the same runtime UUID-v4 key.
Cleanup verified after both paths; credentials and raw idempotency keys are excluded.
DAY30_FAILURE_INJECTION=EXECUTED
DAY30_RUN=PASS
Failure-injection summary JSON: {"redacted":true,"result":"EXPECTED_FAILURE","failureInjection":true,"autoFromSchedule":true,"preAdvanceBeyondThirtyMinutes":true,"tripStates":["SCHEDULED","BOARDING","IN_PROGRESS","COMPLETED"],"parcelStates":["PENDING","LOADED","IN_TRANSIT","UNLOADED"],"polling":{"scheduleGeneration":{"intervalMs":500,"timeoutMs":30000},"autoBoarding":{"intervalMs":500,"timeoutMs":960000},"eventConsumption":{"intervalMs":500,"timeoutMs":45000}},"outboxCounts":{"trip.trip.boarding_started":1,"trip.trip.started":1,"parcel.parcel.loaded":1,"trip.stop.arrived":1,"parcel.parcel.unloaded":1,"trip.trip.completed":1},"duplicateCounts":{"trip.trip.boarding_started":0,"trip.trip.started":0,"parcel.parcel.loaded":0,"trip.stop.arrived":0,"parcel.parcel.unloaded":0,"trip.trip.completed":0},"replayCount":1,"duplicateTransitionCount":0,"duplicateOutboxCount":0,"cleanupResidue":0}
Normal run summary JSON: {"redacted":true,"result":"PASS","failureInjection":false,"autoFromSchedule":true,"preAdvanceBeyondThirtyMinutes":true,"tripStates":["SCHEDULED","BOARDING","IN_PROGRESS","COMPLETED"],"parcelStates":["PENDING","LOADED","IN_TRANSIT","UNLOADED"],"polling":{"scheduleGeneration":{"intervalMs":500,"timeoutMs":30000},"autoBoarding":{"intervalMs":500,"timeoutMs":960000},"eventConsumption":{"intervalMs":500,"timeoutMs":45000}},"outboxCounts":{"trip.trip.boarding_started":1,"trip.trip.started":1,"parcel.parcel.loaded":1,"trip.stop.arrived":1,"parcel.parcel.unloaded":1,"trip.trip.completed":1},"duplicateCounts":{"trip.trip.boarding_started":0,"trip.trip.started":0,"parcel.parcel.loaded":0,"trip.stop.arrived":0,"parcel.parcel.unloaded":0,"trip.trip.completed":0},"replayCount":1,"duplicateTransitionCount":0,"duplicateOutboxCount":0,"cleanupResidue":0}

Assertions passed: 108
Final result: PASS
