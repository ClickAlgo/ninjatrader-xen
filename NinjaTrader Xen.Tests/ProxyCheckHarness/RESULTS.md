# Live investigation results: 104.255.98.77

## Post-upgrade verification

At 05:54:59 UTC on 4 September 2026, the upgraded NinjaTrader `CheckNetworkAsync` method queried v3 and returned **BLOCK**. HTTP 200/status ok, detections.proxy=true, scraper=true, anonymous=true, risk=100, country=US, network.type=Business, operator.name=Rayobyte. All 14 focused v3 tests passed. This verifies local code against the live provider; the application has not been deployed. The sections below preserve the pre-upgrade findings.

Run on 4 September 2026 from the development machine using the configured ProxyCheck key. No database connection or credit grant was performed. Production source/configuration and the historical grant-time response remain unverified.

## Actual NinjaTrader method, v2

At 05:48:01 UTC, the harness invoked `FreeTrialService.CheckNetworkAsync` with the existing six-second client timeout, User-Agent, v2 endpoint, `vpn=1&asn=1&risk=1`, and configured risk threshold 50.

HTTP 200, API status `ok`. Relevant response fields:

```json
{
  "asn": "AS400287",
  "provider": "City of Pharr TX",
  "organisation": "City of Pharr TX",
  "range": "104.255.98.77/30",
  "isocode": "US",
  "proxy": "no",
  "type": "Business",
  "risk": 0
}
```

Actual network decision: **NOT BLOCKED**. This successful response does not require a fail-open error to explain the decision. Other credit eligibility checks were not invoked.

## v3 comparison

At 05:48:24 UTC, a separate read-only request to v3 for the same IP using the same configured key returned HTTP 200, API status `ok`:

```json
{
  "network": { "asn": "AS400287", "provider": "City of Pharr TX", "type": "Business" },
  "detections": {
    "proxy": true,
    "vpn": false,
    "compromised": false,
    "scraper": true,
    "tor": false,
    "hosting": false,
    "anonymous": true,
    "risk": 100,
    "confidence": 100,
    "first_seen": "2026-03-21T10:26:48Z",
    "last_seen": "2026-09-04T05:46:50Z",
    "times_seen": 222
  },
  "operator": { "name": "Rayobyte" }
}
```

These are selected response fields, not full response bodies. The v3 request was a data comparison; the v2 parser was not pointed at v3.

## Conclusion and limits

The current v2/v3 discrepancy is confirmed. The current NinjaTrader network admission is reproduced using the actual screening code. Missing Rayobyte-name matching is not necessary to explain this result: v2 itself supplied proxy=no and risk=0. The origin of the provider discrepancy (version-specific data, caching or account/provider rules) is not established.

This does not prove what the live server received at the reported 02:20 UTC grant time. Investigate the discrepancy with ProxyCheck before choosing a fix. A v3 migration would require a corresponding nested-field parser update; changing the URL alone would not be correct.

## Offline check

The synthetic `risk-100.json` fixture returned **BLOCK** using the same method with proxy=no, type=Business and risk=100. This verifies the risk threshold independently of the live provider classification. It is synthetic test data, not an incident response.
