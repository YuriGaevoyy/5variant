"""Live HTTPS smoke check for a clean SQLite database. Run while DeliveryService.Api is listening."""
import json
import ssl
import sys
import urllib.error
import urllib.request

BASE = "https://127.0.0.1:7135"
CONTEXT = ssl._create_unverified_context()
checks = []


def call(method, path, token=None, body=None):
    data = None if body is None else json.dumps(body).encode("utf-8")
    request = urllib.request.Request(BASE + path, data=data, method=method)
    request.add_header("Content-Type", "application/json")
    if token:
        request.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(request, context=CONTEXT, timeout=10) as response:
            raw = response.read().decode("utf-8")
            return response.status, json.loads(raw) if raw and raw.startswith(("{", "[")) else raw
    except urllib.error.HTTPError as error:
        raw = error.read().decode("utf-8")
        return error.code, json.loads(raw) if raw and raw.startswith(("{", "[")) else raw


def check(name, condition, detail=""):
    checks.append((name, bool(condition), detail))
    print(("PASS " if condition else "FAIL ") + name + (" — " + str(detail) if detail else ""))


def login(user, password):
    status, result = call("POST", "/api/auth/login", body={"login": user, "password": password})
    check("login " + user, status == 200, status)
    return result["token"]


admin = login("admin", "Admin@2026!")
operator = login("operator", "Operator@2026!")
first = login("courier1", "Courier1@2026!")
second = login("courier2", "Courier2@2026!")

status, all_rows = call("GET", "/api/shipments", admin)
check("admin sees demo shipments", status == 200 and len(all_rows) == 9, len(all_rows))
status, own_first = call("GET", "/api/shipments", first)
status, own_second = call("GET", "/api/shipments", second)
check("courier1 sees 3 assigned shipments", len(own_first) == 3, len(own_first))
check("courier2 sees 2 assigned shipments", len(own_second) == 2, len(own_second))
check("courier card is object-restricted",
      call("GET", "/api/shipments/" + str(own_second[0]["id"]), first)[0] == 404)
check("courier statistics are filtered",
      call("GET", "/api/statistics", first)[1]["total"] == 3)
check("courier export forbidden", call("GET", "/api/shipments/export?format=csv", first)[0] == 403)
check("operator cannot list users", call("GET", "/api/users", operator)[0] == 403)
check("courier cannot list recipients", call("GET", "/api/recipients", first)[0] == 403)

recipient = call("GET", "/api/recipients", operator)[1][0]["id"]
courier = call("GET", "/api/couriers", operator)[1][0]["id"]
status, created = call("POST", "/api/shipments", operator,
                       {"recipientId": recipient, "description": "Проверка API", "deliveryAddress": "ул. Тестовая, 1",
                        "weightKg": 1.5, "declaredAmount": 2400, "courierId": None, "notes": ""})
check("create shipment returns 201", status == 201, status)
path = "/api/shipments/" + str(created["id"])
check("in transit requires courier",
      call("POST", path + "/transition", operator,
           {"status": "InTransit", "reason": None, "confirmation": None, "version": 1})[0] == 400)
status, current = call("GET", path, operator)
check("assign courier with version", call("PUT", path, operator,
      {**current, "courierId": courier})[0] in (200, 204))
current = call("GET", path, operator)[1]
check("accept shipment", call("POST", path + "/transition", operator,
      {"status": "Accepted", "reason": None, "confirmation": None, "version": current["version"]})[0] == 204)
current = call("GET", path, operator)[1]
check("move to in transit", call("POST", path + "/transition", operator,
      {"status": "InTransit", "reason": None, "confirmation": None, "version": current["version"]})[0] == 204)
current = call("GET", path, operator)[1]
check("delivery requires confirmation", call("POST", path + "/transition", operator,
      {"status": "Delivered", "reason": None, "confirmation": " ", "version": current["version"]})[0] == 400)
check("deliver with confirmation", call("POST", path + "/transition", operator,
      {"status": "Delivered", "reason": None, "confirmation": "Проверка получена",
       "version": current["version"]})[0] == 204)
current = call("GET", path, operator)[1]
check("return from delivered", call("POST", path + "/transition", operator,
      {"status": "Returned", "reason": "Отказ получателя", "confirmation": None,
       "version": current["version"]})[0] == 204)

bad_csv = "Name,Phone,Address,Email\nПётр,+79991112233,ул. Садовая,ok@example.test\n,123,ул. Новая,ok2@example.test"
status, problem = call("POST", "/api/recipients/import", operator, {"csv": bad_csv})
check("CSV import reports line 3", status == 400 and "Строка 3" in json.dumps(problem, ensure_ascii=False), status)
good_csv = 'Name,Phone,Address,Email\nМария,+79992223344,"ул. Новая, 7",maria@example.test'
status, imported = call("POST", "/api/recipients/import", operator, {"csv": good_csv})
check("valid CSV imports", status == 200 and imported["imported"] == 1, status)
for fmt, marker in (("csv", "Number;Recipient"), ("xml", "<shipments>")):
    status, body = call("GET", "/api/shipments/export?format=" + fmt, operator)
    check("export " + fmt, status == 200 and marker in body, len(body))
status, json_rows = call("GET", "/api/shipments/export?format=json", operator)
check("export json", status == 200 and isinstance(json_rows, list) and "status" in json_rows[0], len(json_rows))

check("logout revokes token", call("POST", "/api/auth/logout", first)[0] == 200 and
      call("GET", "/api/shipments", first)[0] == 401)

failed = [name for name, ok, _ in checks if not ok]
print(f"\nTOTAL {len(checks)}  PASSED {len(checks) - len(failed)}  FAILED {len(failed)}")
sys.exit(1 if failed else 0)
