# SMShell
PoC for an SMS-based shell. Send commands and receive responses over SMS from mobile broadband capable computers.

This tool came as an insipiration during a research on eSIM security implications led by Markus Vervier, presented at [Offensivecon 2023](https://www.offensivecon.org/speakers/2023/markus-vervier.html)

# Disclaimer
This is not a complete C2 but rather a simple Proof of Concept for executing commands remotely over SMS.

# Requirements
For the shell to work you need to devices capable of sending SMS. The victim's computer should be equiped with WWAN module with either a physical SIM or eSIM deployed. 

On the operator's end, two tools are provided:
* .NET binary which uses an embedded WWAN module
* Python script which uses an external Huaweu MiFi thourgh its API

Of course, you could in theory use any online SMS provider on the operator's end via their API.

# Usage
On the victim simply execute the `client-agent.exe` binary. If the agent is compiled as a `Console Application` you should see some verbose messages. If it's compiled as a `Windows Application` (best for real engagements), there will be no GUI.

The operator must specify the victim's phone number as a parameter:

```
server-console.exe +306912345678
```

Whereas if you use the python script you must additionally specify the MiFi details:

```
python3 server-console.py --mifi-ip 192.168.0.1 --mifi-username admin --mifi-password 12345678 --number +306912345678 -v
```

A demo as presented by Markus at Offensive is shown below. On the left is the operator's VM with a MiFi attached, whereas on the right window is client agent.

![Animation](https://github.com/persistent-security/SMShell/assets/134269747/a37731a0-54ce-458f-8bee-131592407d56)
