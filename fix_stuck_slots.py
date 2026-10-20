import os, sqlite3, json

# always look for the database next to this script, wherever it is started from
DB_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "bcp_parking.db")

if not os.path.exists(DB_PATH):
    print("ERROR: bcp_parking.db not found in this folder:")
    print(os.path.dirname(DB_PATH))
    print("Put fix_stuck_slots.py in the same folder as bcp_parking.db.")
    input("\nPress Enter to close...")
    raise SystemExit(1)

conn = sqlite3.connect(DB_PATH)
row = conn.execute("SELECT state_json FROM app_state WHERE id=1").fetchone()
state = json.loads(row[0])

def is_inside(log):
    # new server format uses status="inside"; old test format has no status and no timeOut
    if log.get("status"):
        return log["status"] == "inside"
    return not (log.get("timeOut") or log.get("out"))

# 1. remove stale "inside" records that came from the old test data (no status field)
before = len(state["logs"])
state["logs"] = [l for l in state["logs"] if l.get("status") or l.get("timeOut") or l.get("out")]
removed = before - len(state["logs"])

# 2. free every slot that is marked occupied but has no vehicle really inside
inside_slots = {(l.get("slot") or l.get("slotId")) for l in state["logs"] if is_inside(l)}
freed = []
for slot in state["slots"]:
    if slot.get("status") == "occupied" and slot["id"] not in inside_slots:
        slot["status"] = "vacant"
        slot["plate"] = None
        freed.append(slot["id"])

conn.execute("UPDATE app_state SET state_json=? WHERE id=1", (json.dumps(state),))
conn.commit()
conn.close()

print("Stale records removed:", removed)
print("Slots freed:", freed if freed else "none")

input("\nDone. Press Enter to close...")