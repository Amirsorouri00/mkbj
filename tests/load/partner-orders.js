// Single-order latency for the partner path, under a declared load.
//
// This measures the whole request, not the composition engine: a live price quote, the engine,
// the order write, the invoice and the response. The engine on its own is measured by
// tools/CompositionSimulator, which is pure and runs in under a millisecond — so anything this
// reports above that is the cost of being a web application, and that is the number the PRD's
// p95 target is about.
//
// Each virtual user composes a different amount in a different currency, because a load test
// that asks for the same basket a thousand times measures a cache.
//
//   k6 run tests/load/partner-orders.js
//     BASE_URL     default http://127.0.0.1:18080
//     API_KEY      required — a partner key with orders:create and customers:write
//     RATE         default 90 orders/second — keep it under the partner's rate limit
//     VUS          default 10 pre-allocated, up to MAX_VUS (default 50)
//     DURATION     default 5m
//     CURRENCIES   default USD,EUR,SGD,AED,GBP

import http from 'k6/http';
import { check } from 'k6';
import { Trend, Rate } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://127.0.0.1:18080';
const API_KEY = __ENV.API_KEY;
const CURRENCIES = (__ENV.CURRENCIES || 'USD,EUR,SGD,AED,GBP').split(',');

const composed = new Trend('order_composed_ms', true);
const refused = new Rate('order_refused');

export const options = {
  // A constant arrival rate, not a VU loop. Ten VUs looping as fast as they can issue roughly
  // 2,000 orders a second against a partner whose own ceiling is 6,000 a minute, so 95% of the
  // run comes back 429 and the percentiles describe the rate limiter. Pacing the arrival below
  // the partner's limit measures the thing the PRD sets a target for.
  scenarios: {
    create_orders: {
      executor: 'constant-arrival-rate',
      rate: Number(__ENV.RATE || 90),
      timeUnit: '1s',
      duration: __ENV.DURATION || '5m',
      preAllocatedVUs: Number(__ENV.VUS || 10),
      maxVUs: Number(__ENV.MAX_VUS || 50),
    },
  },

  // min and max as well as the percentiles: an average that looks fine while the worst request
  // took forty seconds is the shape of failure this is meant to catch.
  summaryTrendStats: ['avg', 'min', 'med', 'p(95)', 'p(99)', 'max'],

  thresholds: {
    // The PRD's target, stated as a threshold so the run fails rather than being interpreted.
    'http_req_duration{name:create_order}': ['p(95)<10000'],
    'order_refused': ['rate<0.01'],
    ...Object.fromEntries(CURRENCIES.map((c) => [`checks{currency:${c}}`, ['rate>0.99']])),
  },
};

export function setup() {
  if (!API_KEY) throw new Error('API_KEY is required — issue a partner key first.');

  return { startedAt: new Date().toISOString() };
}

/**
 * Log-uniform over 10.00–1,000,000.00, matching the simulator's distribution so the two reports
 * describe the same workload. Exact cents, because a round hundred is the easy case.
 */
function amount() {
  const minor = Math.round(Math.pow(10, 3 + (Math.random() * 5)));
  const clamped = Math.min(Math.max(minor, 1000), 100000000);

  return (clamped / 100).toFixed(2);
}

export default function () {
  const currency = CURRENCIES[Math.floor(Math.random() * CURRENCIES.length)];

  // Unique per request: externalOrderId is the idempotency key, so reusing one would measure the
  // replay path — a cheap lookup — and report it as the cost of composing an order.
  const externalOrderId = `LOAD-${__VU}-${__ITER}-${Date.now()}`;

  const payload = JSON.stringify({
    external_order_id: externalOrderId,
    external_customer_id: `LOADC-${__VU}`,
    amount: amount(),
    currency: currency,
    customer: {
      email: `load-${__VU}@example.invalid`,
      phone: '+6580000000',
      full_name: `Load VU ${__VU}`,
    },
  });

  const response = http.post(`${BASE_URL}/api/v1/orders`, payload, {
    headers: {
      Authorization: `Bearer ${API_KEY}`,
      'Content-Type': 'application/json',
    },
    tags: { name: 'create_order', currency: currency },
  });

  const created = response.status === 200 || response.status === 201;


  // Two 422s are policy, not faults, and counting them as failures would make the seller's own
  // rules look like an outage:
  //
  //   EXACT_COMPOSITION_UNAVAILABLE — the catalogue cannot total that amount.
  //   AMOUNT_OUT_OF_RANGE           — the seller's minimum and maximum, held in USD. This draw
  //                                   picks an amount in the local currency, so the bottom of
  //                                   the AED range sits below a ten-dollar floor while the same
  //                                   figure in USD clears it. That asymmetry is the limit doing
  //                                   its job, not the order path failing.
  const body = String(response.body);
  const refusedByPolicy =
    response.status === 422 &&
    (body.includes('EXACT_COMPOSITION_UNAVAILABLE') || body.includes('AMOUNT_OUT_OF_RANGE'));

  check(response, {
    'composed or legitimately refused': () => created || refusedByPolicy,
  }, { currency: currency });

  refused.add(!created && !refusedByPolicy);
  if (created) composed.add(response.timings.duration);
}
