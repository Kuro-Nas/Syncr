import logging
import time

# For pymodbus 2.x, imports are under .sync
try:
    from pymodbus.client.sync import ModbusSerialClient
except ImportError:
    # Fallback for pymodbus 3.x
    from pymodbus.client import ModbusSerialClient

# Configure logging
logging.basicConfig(level=logging.INFO)
log = logging.getLogger(__name__)

def run_master():
    # 1. Configure the Serial Client
    # Increase timeout and add retries for unstable connections
    try:
        client = ModbusSerialClient(
            method='rtu',
            port="/dev/ttyAMA0",
            baudrate=9600,
            parity='N',
            stopbits=1,
            bytesize=8,
            timeout=3,        # Increased from 1 to 3
            retries=3,        # Added retries
            retry_on_empty=True
        )
    except TypeError:
        # Fallback for 3.x constructor
        client = ModbusSerialClient(
            port="/dev/ttyAMA0",
            baudrate=9600,
            parity='N',
            stopbits=1,
            bytesize=8,
            timeout=3,
            retries=3
        )

    print("--- Modbus RTU Master (Robust Mode) Starting ---")
    print("Connecting to /dev/ttyAMA0...")

    if not client.connect():
        print("Failed to connect to the serial port. Check your wiring and port name.")
        return

    try:
        while True:
            # 2. Read Holding Registers (Slave ID 1, Start Address 0, Count 3)
            try:
                # Add a small delay BEFORE reading to ensure line is quiet
                time.sleep(0.1)
                response = client.read_holding_registers(address=0, count=3, unit=1)
            except TypeError:
                response = client.read_holding_registers(address=0, count=3, slave=1)

            if not response.isError():
                v = response.registers[0]
                i = response.registers[1]
                p = response.registers[2]
                print(f"[{time.strftime('%H:%M:%S')}] OK -> Voltage: {v}V, Current: {i}A, Power: {p}W")
            else:
                print(f"[{time.strftime('%H:%M:%S')}] Warning: {response}")
                # If we get errors, wait a bit longer before next attempt
                time.sleep(1)

            time.sleep(1)
            
    except KeyboardInterrupt:
        print("Stopping Master...")
    finally:
        client.close()

if __name__ == "__main__":
    run_master()
