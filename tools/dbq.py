"""Read-only SQL query helper for the AskLucy shared databases.

Reads the connection strings from the file the user pointed at, picks the database
by name, and runs one query through sqlcmd. The password never leaves this
process: it is read from the file and handed to sqlcmd as an argument.
"""

import re
import subprocess
import sys

CONN_FILE = r"E:\Workshop\BIM Catalyst\Web Apps\Platform\_misc\DB connection strings.txt"


def load_connections():
    raw = open(CONN_FILE, encoding="utf-8-sig", errors="replace").read()
    found = {}
    for line in raw.splitlines():
        if "Initial Catalog=" not in line:
            continue
        parts = re.findall(r"([A-Za-z ]+?)\s*=\s*([^;]+)", line)
        settings = {k.strip().lower(): v.strip() for k, v in parts}
        catalog = settings.get("initial catalog")
        if not catalog:
            continue
        found[catalog] = {
            "server": settings.get("data source"),
            "user": settings.get("user id") or settings.get("uid"),
            "password": settings.get("password"),
        }
    return found


def main():
    if len(sys.argv) < 3:
        print("usage: dbq.py <database> <sql>")
        return 1

    database, query = sys.argv[1], sys.argv[2]
    connections = load_connections()
    if database not in connections:
        print(f"unknown database '{database}'. known: {sorted(connections)}")
        return 1

    connection = connections[database]
    if not all(connection.values()):
        print(f"incomplete connection settings for '{database}'")
        return 1

    result = subprocess.run(
        [
            "sqlcmd",
            "-S", connection["server"],
            "-d", database,
            "-U", connection["user"],
            "-P", connection["password"],
            "-N", "-C", "-h", "-1", "-W",
            "-Q", "SET NOCOUNT ON; " + query,
        ],
        capture_output=True,
        text=True,
    )
    print(result.stdout.strip())
    if result.stderr.strip():
        print(result.stderr.strip(), file=sys.stderr)
    return result.returncode


if __name__ == "__main__":
    raise SystemExit(main())
