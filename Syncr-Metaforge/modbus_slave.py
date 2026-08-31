import logging
import threading
import time
import math
import random

# For pymodbus 2.x, imports are under .sync or .asynchronous
try:
    from pymodbus.server.sync import StartSerialServer
except ImportError:
    from pymodbus.server import StartSerialServer

from pymodbus.datastore import ModbusSequentialDataBlock, ModbusSlaveContext, ModbusServerContext
from pymodbus.transaction import ModbusRtuFramer

# --- SIMULATION LOGGING ---
logging.basicConfig(level=logging.ERROR)
log = logging.getLogger("SyncrSlave")

def updater(context):
    """
    Simulates varied industrial data across multiple registers and slave IDs
    """
    start_time = time.time()
    
    while True:
        elapsed = time.time() - start_time
        
        # Update Slaves 1-5 if they exist in the context
        for slave_id in [1, 2, 3, 4, 5]:
            if slave_id not in context:
                continue
                
            store = context[slave_id]
            
            # Generate values for registers 0-99
            values = []
            for addr in range(100):
                offset = slave_id * 10
                
                if addr < 10: # Power-related
                    v = 230 + math.sin(elapsed * 2.0 + offset) * 5 + random.uniform(-5, 5)
                    i = 15 + math.cos(elapsed * 2.5 + offset) * 2 + random.uniform(-2, 2)
                    p = v * i
                    if addr == 0: val = v
                    elif addr == 1: val = i
                    elif addr == 2: val = p
                    else: val = 50 + addr
                elif addr < 20: # Environmental
                    val = 45 + math.sin(elapsed * 1.5 + offset) * 10 + random.uniform(-4, 4)
                elif addr < 30: # Mechanical
                    val = 1440 + math.sin(elapsed * 3.0 + offset) * 100 + random.uniform(-50, 50)
                else: # Generic counters
                    val = (int(elapsed) + addr + offset) % 1000
                
                values.append(int(val))
            
            # Write to Holding Registers (3)
            store.setValues(3, 0, values)
        
        time.sleep(0.5)

def run_server():
    # 1. Define Data Store for multiple Slave IDs
    slaves = {}
    for i in range(1, 6):
        slaves[i] = ModbusSlaveContext(hr=ModbusSequentialDataBlock(0, [0]*100))
        
    context = ModbusServerContext(slaves=slaves, single=False)
    
    # 2. Start Simulation Thread
    thread = threading.Thread(target=updater, args=(context,), daemon=True)
    thread.start()
    
    print("\n" + "="*60)
    print("      SYNCR MULTI-MACHINE MODBUS RTU SLAVE SIMULATOR")
    print("      MODE: SLAVE ID 1-5 | PORT: COM3 | BAUD: 9600")
    print("="*60)
    print("Simulating Voltage(0), Current(1), Power(2) + Multi-register data...")
    print("Listening for requests...\n")

    StartSerialServer(
        context=context,
        framer=ModbusRtuFramer,
        port='COM3',
        baudrate=9600,
        parity='N',
        stopbits=1,
        bytesize=8,
        timeout=0.1
    )

if __name__ == "__main__":
    run_server()
