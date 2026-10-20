import sqlite3
from pwdlib import PasswordHash

DB_PATH = "bcp_parking.db"

password_hash = PasswordHash.recommended()

new_password = "Guard123"

conn = sqlite3.connect(DB_PATH)
cursor = conn.cursor()

hashed = password_hash.hash(new_password)

cursor.execute("""
    UPDATE auth_users
    SET password_hash = ?
    WHERE username = ?
""", (hashed, "guard1"))

conn.commit()

if cursor.rowcount == 0:
    print("ERROR: guard1 account was not found.")
else:
    print("guard1 password has been reset.")
    print("Username: guard1")
    print("Password: Guard123")

conn.close()