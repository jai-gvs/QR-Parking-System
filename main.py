from pathlib import Path
import json
import sqlite3
from datetime import datetime, timedelta, timezone
from typing import Optional
import random

from fastapi import FastAPI, HTTPException, Depends
from fastapi.middleware.cors import CORSMiddleware
from fastapi.security import HTTPBearer, HTTPAuthorizationCredentials
from pydantic import BaseModel
from jose import jwt, JWTError
from pwdlib import PasswordHash


# ============================================================
# BASIC SETTINGS
# ============================================================

BASE_DIR = Path(__file__).resolve().parent
DB_PATH = BASE_DIR / "bcp_parking.db"

SECRET_KEY = "BCP-GATE-DEMO-CHANGE-THIS-SECRET-KEY"
ALGORITHM = "HS256"
ACCESS_TOKEN_EXPIRE_MINUTES = 60

password_hash = PasswordHash.recommended()
security = HTTPBearer()


# ============================================================
# FASTAPI APP
# ============================================================

app = FastAPI(title="BCP QR Vehicle & Parking API")


app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


# ============================================================
# DEFAULT PARKING SYSTEM DATA
# ============================================================

def default_state():
    slots = []

    for zone in ("A", "B", "C"):
        for i in range(1, 9):
            slots.append({
                "id": f"{zone}{i}",
                "zone": zone,
                "status": "vacant",
                "plate": None
            })

    return {
        "students": [
            {
                "studentId": "2023-00145",
                "name": "Juan Dela Cruz",
                "course": "BSCpE"
            },
            {
                "studentId": "2023-00298",
                "name": "Maria Santos",
                "course": "BSIT"
            }
        ],

        "vehicles": [
            {
                "plate": "ABC-1234",
                "studentId": "2023-00145",
                "type": "Motorcycle",
                "qrToken": "BCP-QR-000001",
                "status": "active"
            },
            {
                "plate": "XYZ-5678",
                "studentId": "2023-00298",
                "type": "Sedan",
                "qrToken": "BCP-QR-000002",
                "status": "active"
            }
        ],

        "slots": slots,

        "logs": [],

        "users": [
            {
                "username": "admin",
                "name": "System Administrator",
                "role": "admin"
            },
            {
                "username": "guard1",
                "name": "Security Guard 1",
                "role": "attendant"
            }
        ],

        "rates": {
            "firstHour": 20,
            "extraHour": 10
        },

        "seq": 3
    }


# ============================================================
# DATABASE SETUP
# ============================================================

def init_db():
    with sqlite3.connect(DB_PATH) as conn:

        # Existing parking-system state
        conn.execute("""
            CREATE TABLE IF NOT EXISTS app_state (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                state_json TEXT NOT NULL
            )
        """)

        row = conn.execute(
            "SELECT state_json FROM app_state WHERE id=1"
        ).fetchone()

        if row is None:
            conn.execute(
                """
                INSERT INTO app_state(id, state_json)
                VALUES(1, ?)
                """,
                (json.dumps(default_state()),)
            )

        # ====================================================
        # NEW AUTHENTICATION TABLE
        # ====================================================

        conn.execute("""
            CREATE TABLE IF NOT EXISTS auth_users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT UNIQUE NOT NULL,
                password_hash TEXT NOT NULL,
                name TEXT NOT NULL,
                role TEXT NOT NULL,
                active INTEGER NOT NULL DEFAULT 1
            )
        """)

        # Column that lets the admin see passwords (plain text - see note)
        cols=[r[1] for r in conn.execute("PRAGMA table_info(auth_users)")]
        if "plain_password" not in cols:
            conn.execute("ALTER TABLE auth_users ADD COLUMN plain_password TEXT")

        conn.commit()

    seed_auth_users()
    backfill_known_passwords()


# ============================================================
# CREATE TEST LOGIN USERS
# ============================================================

def seed_auth_users():

    test_users = [
        {
            "username": "admin",
            "password": "Admin@123",
            "name": "System Administrator",
            "role": "admin"
        },
        {
            "username": "guard1",
            "password": "Guard@123",
            "name": "Security Guard 1",
            "role": "attendant"
        }
    ]

    with sqlite3.connect(DB_PATH) as conn:

        # Students no longer log in: remove any leftover student accounts
        conn.execute("DELETE FROM auth_users WHERE role='student'")

        for user in test_users:

            existing = conn.execute(
                "SELECT id FROM auth_users WHERE username=?",
                (user["username"],)
            ).fetchone()

            if existing is None:

                hashed_password = password_hash.hash(
                    user["password"]
                )

                conn.execute(
                    """
                    INSERT INTO auth_users
                    (username, password_hash, name, role, active, plain_password)
                    VALUES (?, ?, ?, ?, 1, ?)
                    """,
                    (
                        user["username"],
                        hashed_password,
                        user["name"],
                        user["role"],
                        user["password"]
                    )
                )

        conn.commit()


# ------------------------------------------------------------
# BACK-FILL PASSWORDS FOR OLD ACCOUNTS
# Tries the known default passwords against each stored hash; when one
# matches, that password is saved so the admin can see it.
# ------------------------------------------------------------

def backfill_known_passwords():
    candidates = {
        "admin": ["Admin@123", "admin123", "Admin123"],
        "guard1": ["Guard@123", "Guard123"],
    }

    with sqlite3.connect(DB_PATH) as conn:
        rows = conn.execute(
            """SELECT username, password_hash FROM auth_users
               WHERE plain_password IS NULL"""
        ).fetchall()

        for username, stored_hash in rows:
            for pw in candidates.get(username, []):
                try:
                    ok = password_hash.verify(pw, stored_hash)
                except Exception:
                    ok = False
                if ok:
                    conn.execute(
                        "UPDATE auth_users SET plain_password=? WHERE username=?",
                        (pw, username)
                    )
                    break

        conn.commit()


# ============================================================
# STATE FUNCTIONS
# ============================================================

def get_state():

    with sqlite3.connect(DB_PATH) as conn:

        row = conn.execute(
            "SELECT state_json FROM app_state WHERE id=1"
        ).fetchone()

    if not row:
        init_db()
        return default_state()

    return json.loads(row[0])


def put_state(state):

    with sqlite3.connect(DB_PATH) as conn:

        conn.execute(
            """
            UPDATE app_state
            SET state_json=?
            WHERE id=1
            """,
            (json.dumps(state),)
        )

        conn.commit()


# ============================================================
# AUTHENTICATION MODELS
# ============================================================

class LoginRequest(BaseModel):
    username: str
    password: str


class StatePayload(BaseModel):
    students: list
    vehicles: list
    slots: list
    logs: list
    users: list
    rates: dict
    seq: int


# ============================================================
# JWT FUNCTIONS
# ============================================================

def create_access_token(username: str, role: str):

    expire = datetime.now(timezone.utc) + timedelta(
        minutes=ACCESS_TOKEN_EXPIRE_MINUTES
    )

    payload = {
        "sub": username,
        "role": role,
        "exp": expire
    }

    return jwt.encode(
        payload,
        SECRET_KEY,
        algorithm=ALGORITHM
    )


def get_current_user(
    credentials: HTTPAuthorizationCredentials = Depends(security)
):

    token = credentials.credentials

    try:

        payload = jwt.decode(
            token,
            SECRET_KEY,
            algorithms=[ALGORITHM]
        )

        username = payload.get("sub")
        role = payload.get("role")

        if not username or not role:
            raise HTTPException(
                status_code=401,
                detail="Invalid authentication token"
            )

        return {
            "username": username,
            "role": role
        }

    except JWTError:

        raise HTTPException(
            status_code=401,
            detail="Invalid or expired authentication token"
        )


# ============================================================
# STARTUP
# ============================================================

@app.on_event("startup")
def startup():

    init_db()


# ============================================================
# HEALTH CHECK
# ============================================================

@app.get("/api/health")
def health():

    return {
        "status": "ok",
        "database": str(DB_PATH)
    }


# ============================================================
# LOGIN
# ============================================================

@app.post("/api/login")
def login(request: LoginRequest):

    with sqlite3.connect(DB_PATH) as conn:

        row = conn.execute(
            """
            SELECT username, password_hash, name, role, active
            FROM auth_users
            WHERE username=?
            """,
            (request.username,)
        ).fetchone()

    if not row:

        raise HTTPException(
            status_code=401,
            detail="Invalid username or password"
        )

    username, stored_hash, name, role, active = row

    if not active:

        raise HTTPException(
            status_code=403,
            detail="User account is disabled"
        )

    if not password_hash.verify(
        request.password,
        stored_hash
    ):

        raise HTTPException(
            status_code=401,
            detail="Invalid username or password"
        )

    token = create_access_token(
        username,
        role
    )

    return {
        "access_token": token,
        "token_type": "bearer",
        "user": {
            "username": username,
            "name": name,
            "role": role
        }
    }


# ============================================================
# CURRENT USER
# ============================================================

@app.get("/api/me")
def current_user(
    user=Depends(get_current_user)
):

    return user

# ============================================================
# ADMIN USER MANAGEMENT
# ============================================================

class CreateUserRequest(BaseModel):
    username: str
    password: str
    name: str
    role: str


@app.post("/api/users")
def create_user(
    request: CreateUserRequest,
    current_user=Depends(get_current_user)
):
    # Only administrators can create accounts
    if current_user["role"] != "admin":
        raise HTTPException(
            status_code=403,
            detail="Administrator access required"
        )

    username = request.username.strip()
    password = request.password
    name = request.name.strip()
    role = request.role.strip().lower()

    # Basic validation
    if not username:
        raise HTTPException(
            status_code=400,
            detail="Username is required"
        )

    if len(password) < 6:
        raise HTTPException(
            status_code=400,
            detail="Password must be at least 6 characters"
        )

    if not name:
        raise HTTPException(
            status_code=400,
            detail="Full name is required"
        )

    if role not in ["admin", "attendant"]:
        raise HTTPException(
            status_code=400,
            detail="Invalid role"
        )

    # Check whether username already exists
    with sqlite3.connect(DB_PATH) as conn:
        existing = conn.execute(
            "SELECT id FROM auth_users WHERE username=?",
            (username,)
        ).fetchone()

        if existing:
            raise HTTPException(
                status_code=409,
                detail="Username already exists"
            )

        # Hash the password before saving it
        hashed_password = password_hash.hash(password)

        conn.execute(
            """
            INSERT INTO auth_users
            (username, password_hash, name, role, active, plain_password)
            VALUES (?, ?, ?, ?, 1, ?)
            """,
            (
                username,
                hashed_password,
                name,
                role,
                password
            )
        )

        conn.commit()

    # Also add the account to the parking-system user list
    state = get_state()

    # Remove any accidental duplicate from the display list
    state["users"] = [
        user for user in state["users"]
        if user.get("username") != username
    ]

    state["users"].append({
        "username": username,
        "name": name,
        "role": role
    })

    put_state(state)

    return {
        "ok": True,
        "message": "User account created successfully",
        "user": {
            "username": username,
            "name": name,
            "role": role
        }
    }
# ------------------------------------------------------------
# LIST USERS WITH PASSWORDS (admin only)
# ------------------------------------------------------------

@app.get("/api/users")
def list_users(current_user=Depends(get_current_user)):
    if current_user["role"] != "admin":
        raise HTTPException(status_code=403,
                            detail="Administrator access required")

    with sqlite3.connect(DB_PATH) as conn:
        rows = conn.execute(
            "SELECT username, name, role, plain_password FROM auth_users"
        ).fetchall()

    return [
        {"username": u, "name": n, "role": r, "password": p}
        for u, n, r, p in rows
    ]


class SetPasswordRequest(BaseModel):
    password: str


@app.put("/api/users/{username}/password")
def set_user_password(
    username: str,
    request: SetPasswordRequest,
    current_user=Depends(get_current_user)
):
    if current_user["role"] != "admin":
        raise HTTPException(status_code=403,
                            detail="Administrator access required")

    if len(request.password) < 6:
        raise HTTPException(status_code=400,
                            detail="Password must be at least 6 characters")

    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """UPDATE auth_users
               SET password_hash=?, plain_password=?
               WHERE username=?""",
            (password_hash.hash(request.password), request.password, username)
        )
        conn.commit()

    if cur.rowcount == 0:
        raise HTTPException(status_code=404, detail="User not found")

    return {"ok": True}


# ============================================================
# PARKING STATE
# ============================================================

@app.get("/api/state")
def read_state():

    return get_state()


@app.put("/api/state")
def write_state(
    payload: StatePayload
):

    state = payload.model_dump()

    put_state(state)

    return {
        "ok": True
    }


# ============================================================
# RESET
# ============================================================

@app.post("/api/reset")
def reset_state():

    state = default_state()

    put_state(state)

    return state


# ============================================================
# VEHICLES
# ============================================================

@app.get("/api/vehicles")
def vehicles():

    return get_state()["vehicles"]


# ============================================================
# PARKING SLOTS
# ============================================================

@app.get("/api/slots")
def slots():

    return get_state()["slots"]


# ============================================================
# PARKING LOGS
# ============================================================

@app.get("/api/logs")
def logs():

    return get_state()["logs"]
    # ============================================================
# ENTRANCE / PAYMENT / EXIT WORKFLOW
# ============================================================

class ParkingScanRequest(BaseModel):
    qrToken: str


class PaymentRequest(BaseModel):
    qrToken: str
    amount: float
    method: str = "Cash"
    reference: Optional[str] = None


# ------------------------------------------------------------
# FIND VEHICLE
# ------------------------------------------------------------

def find_vehicle(state, qr_token):

    qr_token = qr_token.strip().upper()

    for vehicle in state["vehicles"]:

        if vehicle.get("qrToken", "").upper() == qr_token:
            return vehicle

        if vehicle.get("plate", "").upper() == qr_token:
            return vehicle

    return None


# ------------------------------------------------------------
# FIND ACTIVE PARKING SESSION
# ------------------------------------------------------------

def find_active_session(state, vehicle):

    for log in reversed(state["logs"]):

        if (
            log.get("plate") == vehicle["plate"]
            and log.get("status") == "inside"
        ):
            return log

    return None


# ------------------------------------------------------------
# ENTRANCE — PREPARE PAYMENT
# ------------------------------------------------------------

@app.post("/api/parking/entry/prepare")
def prepare_entry(
    request: ParkingScanRequest,
    current_user=Depends(get_current_user)
):

    # Only attendants and administrators can process entrances
    if current_user["role"] not in ["attendant", "admin"]:

        raise HTTPException(
            status_code=403,
            detail="Attendant or administrator access required"
        )

    state = get_state()

    vehicle = find_vehicle(
        state,
        request.qrToken
    )

    if not vehicle:

        raise HTTPException(
            status_code=404,
            detail="Vehicle not found"
        )

    # Check whether vehicle is already inside
    active_session = find_active_session(
        state,
        vehicle
    )

    if active_session:

        raise HTTPException(
            status_code=409,
            detail=f"Vehicle {vehicle['plate']} already has an active session inside."
        )

    # Find first vacant slot
    available_slot = None

    for slot in state["slots"]:

        if slot.get("status") == "vacant":

            available_slot = slot
            break

    if not available_slot:

        raise HTTPException(
            status_code=409,
            detail="No parking slots are currently available."
        )

    # Parking fee is collected BEFORE entry
    fee = float(
        state.get("rates", {}).get("firstHour", 20)
    )

    return {
        "ok": True,
        "vehicle": vehicle,
        "slot": available_slot,
        "amount": fee,
        "paymentStatus": "unpaid"
    }


# ------------------------------------------------------------
# PAYMENT AT ENTRANCE
# ------------------------------------------------------------

@app.post("/api/parking/payment")
def parking_payment(
    request: PaymentRequest,
    current_user=Depends(get_current_user)
):

    # Only attendants and administrators can collect payment
    if current_user["role"] not in ["attendant", "admin"]:

        raise HTTPException(
            status_code=403,
            detail="Attendant or administrator access required"
        )

    state = get_state()

    vehicle = find_vehicle(
        state,
        request.qrToken
    )

    if not vehicle:

        raise HTTPException(
            status_code=404,
            detail="Vehicle not found"
        )

    # Determine the required fee
    required_amount = float(
        state.get("rates", {}).get("firstHour", 20)
    )

    # Prevent underpayment
    if float(request.amount) < required_amount:

        raise HTTPException(
            status_code=400,
            detail=f"Payment must be at least ₱{required_amount:.2f}"
        )

    # Check if vehicle is already inside
    active_session = find_active_session(
        state,
        vehicle
    )

    if active_session:

        raise HTTPException(
            status_code=409,
            detail=f"Vehicle {vehicle['plate']} already has an active session."
        )

    # Check available slot
    available_slot = None

    for slot in state["slots"]:

        if slot.get("status") == "vacant":

            available_slot = slot
            break

    if not available_slot:

        raise HTTPException(
            status_code=409,
            detail="No parking slots are currently available."
        )

    now = datetime.now(timezone.utc).astimezone().isoformat()

    # Payment method + reference number
    method = (request.method or "Cash").strip().title()
    if method not in ("Cash", "Gcash", "Maya"):
        method = "Cash"
    if method == "Gcash":
        method = "GCash"

    # Use the reference typed by the attendant (e.g. GCash/Maya ref no.);
    # otherwise generate one automatically.
    reference = (request.reference or "").strip()
    if not reference:
        reference = (
            "PAY-" + datetime.now().strftime("%y%m%d%H%M%S")
            + str(random.randint(10, 99))
        )

    # Mark slot occupied
    available_slot["status"] = "occupied"
    available_slot["plate"] = vehicle["plate"]

    # Create parking session
    session = {
        "id": f"SESSION-{datetime.now().strftime('%Y%m%d%H%M%S')}",
        "plate": vehicle["plate"],
        "qrToken": vehicle["qrToken"],
        "studentId": vehicle.get("studentId"),
        "slot": available_slot["id"],
        "in": now,
        "out": None,
        "fee": required_amount,
        "paidAmount": float(request.amount),
        "paymentStatus": "paid",
        "paymentTime": now,
        "paymentMethod": method,
        "refNumber": reference,
        "status": "inside"
    }

    state["logs"].append(session)

    put_state(state)

    return {
        "ok": True,
        "message": f"Payment received for {vehicle['plate']}.",
        "vehicle": vehicle,
        "slot": available_slot,
        "session": session
    }


# ------------------------------------------------------------
# EXIT — VERIFY PAYMENT
# ------------------------------------------------------------

@app.post("/api/parking/exit/prepare")
def prepare_exit(
    request: ParkingScanRequest,
    current_user=Depends(get_current_user)
):

    # Only attendants and administrators can process exits
    if current_user["role"] not in ["attendant", "admin"]:

        raise HTTPException(
            status_code=403,
            detail="Attendant or administrator access required"
        )

    state = get_state()

    vehicle = find_vehicle(
        state,
        request.qrToken
    )

    if not vehicle:

        raise HTTPException(
            status_code=404,
            detail="Vehicle not found"
        )

    session = find_active_session(
        state,
        vehicle
    )

    if not session:

        raise HTTPException(
            status_code=404,
            detail=f"Vehicle {vehicle['plate']} does not have an active parking session."
        )

    # PAYMENT CHECK
    if session.get("paymentStatus") != "paid":

        raise HTTPException(
            status_code=402,
            detail=(
                f"Payment required. Vehicle {vehicle['plate']} "
                f"has not completed payment."
            )
        )

    return {
        "ok": True,
        "vehicle": vehicle,
        "session": session,
        "paymentStatus": "paid",
        "message": "Payment verified. Exit is allowed."
    }


# ------------------------------------------------------------
# EXIT — COMPLETE EXIT
# ------------------------------------------------------------

@app.post("/api/parking/exit")
def parking_exit(
    request: ParkingScanRequest,
    current_user=Depends(get_current_user)
):

    # Only attendants and administrators can process exits
    if current_user["role"] not in ["attendant", "admin"]:

        raise HTTPException(
            status_code=403,
            detail="Attendant or administrator access required"
        )

    state = get_state()

    vehicle = find_vehicle(
        state,
        request.qrToken
    )

    if not vehicle:

        raise HTTPException(
            status_code=404,
            detail="Vehicle not found"
        )

    session = find_active_session(
        state,
        vehicle
    )

    if not session:

        raise HTTPException(
            status_code=404,
            detail=f"Vehicle {vehicle['plate']} is not currently inside."
        )

    # Never allow unpaid vehicle to exit
    if session.get("paymentStatus") != "paid":

        raise HTTPException(
            status_code=402,
            detail="Payment required before exit."
        )

    now = datetime.now(timezone.utc).astimezone().isoformat()

    # Mark session as completed
    session["out"] = now
    session["status"] = "completed"

    # Free the parking slot
    slot_id = session.get("slot")

    for slot in state["slots"]:

        if slot.get("id") == slot_id:

            slot["status"] = "vacant"
            slot["plate"] = None
            break

    put_state(state)

    return {
        "ok": True,
        "message": f"Vehicle {vehicle['plate']} exited successfully.",
        "vehicle": vehicle,
        "session": session,
        "slotReleased": slot_id
    }