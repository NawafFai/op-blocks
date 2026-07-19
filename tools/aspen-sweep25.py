# -*- coding: utf-8 -*-
"""Aspen V14 live sweep of ALL 25 OP blocks (headless COM).
For each block: physics-appropriate case -> import .inp (CAPE-OPEN by CLSID),
save+reopen, lazy-init, wire named ports, Reinit+Run2, then gate on:
  BLKSTAT==0, mass balance in==out (<1e-6 rel), PARAMSTRING outputs finite.
PER_ERROR is recorded (INFO databank notices legitimately bump it).
Each case runs in a FRESH engine so one failure can't poison the next.
Results: C:\\Users\\Public\\OPBlocks\\sweep\\sweep25.log + per-block .bkp."""
import base64, json, math, os, re, struct, sys, time
import win32com.client as w32

OUT = r"C:\Users\Public\OPBlocks\sweep"
LOG = os.path.join(OUT, "sweep25.log")
os.makedirs(OUT, exist_ok=True)

COMP_DEFS = {
    "WATER": "H2O", "NACL": "NACL", "LICL": "LICL", "MGCL2": "MGCL2",
    "NAOH": "NAOH", "CL2": "CL2", "H2": "H2", "O2": "O2", "N2": "N2",
    "CO2": "CO2", "TOLUENE": "C7H8", "NA2CO3": "NA2CO3",
}

# code, clsid, [(port, stream, {comp: kg/hr}, T C, P bar)] (inlets), [(port, stream)] outlets
CASES = [
 ("OP-RO",  "3EB2EFDD-D0A2-4E21-B9BB-53E1E25EA11F",
   [("FEED","IN1",{"WATER":1524,"NACL":55.3},25,60)],
   [("CONCENTRATE","OU1"),("PERMEATE","OU2")]),
 ("OP-NF",  "74927F7D-A0A8-4B31-B6DF-3A283D5582A5",
   [("FEED","IN1",{"WATER":1000,"NACL":3},25,10)],
   [("CONCENTRATE","OU1"),("PERMEATE","OU2")]),
 ("OP-UF",  "6981B638-5DBE-4647-A20B-2C6B9809A301",
   [("FEED","IN1",{"WATER":1000,"NACL":1},25,2)],
   [("RETENTATE","OU1"),("PERMEATE","OU2")]),
 ("OP-MD",  "091F27E3-70D1-4885-8032-0080C7B214B6",
   [("HOTIN","IN1",{"WATER":1000,"NACL":35},70,1.5),
    ("COLDIN","IN2",{"WATER":1000},20,1.5)],
   [("HOTOUT","OU1"),("COLDOUT","OU2")]),
 ("OP-MED", "32E353BD-1854-447B-A49F-DA2C22AF130C",
   [("FEED","IN1",{"WATER":1000,"NACL":35},25,1.01325)],
   [("DISTILLATE","OU1"),("BRINE","OU2")]),
 ("OP-MSF", "DE8A249A-E088-4F5A-A8C7-73DAF61DD337",
   [("FEED","IN1",{"WATER":1000,"NACL":35},25,1.01325)],
   [("DISTILLATE","OU1"),("BRINE","OU2")]),
 ("OP-MVC", "3F1CC3F7-A38A-45F9-93A8-4962E02378E5",
   [("FEED","IN1",{"WATER":1000,"NACL":35},25,1.01325)],
   [("DISTILLATE","OU1"),("BRINE","OU2")]),
 ("OP-EVAPPOND","23C4D15D-67F2-40F3-AC66-215BDE083047",
   [("BRINEFEED","IN1",{"WATER":10000,"NACL":500},30,1.01325)],
   [("CONCENTRATE","OU1"),("VAPOR","OU2")]),
 ("OP-FO",  "762666CD-FC8B-42E3-8D25-B78CF74EE588",
   [("FEEDIN","IN1",{"WATER":1000,"NACL":35},25,1.5),
    ("DRAWIN","IN2",{"WATER":800,"NACL":160},25,1.5)],
   [("FEEDOUT","OU1"),("DRAWOUT","OU2")]),
 ("OP-PRO", "34D9FFDA-378A-4BB0-9B3B-6437FEC531DC",
   [("FEEDIN","IN1",{"WATER":1000,"NACL":2},25,1.5),
    ("DRAWIN","IN2",{"WATER":800,"NACL":28},25,1.5)],
   [("FEEDOUT","OU1"),("DRAWOUT","OU2")]),
 ("OP-CDI", "F449ED69-B1C4-4C23-816A-29188920ED71",
   [("FEED","IN1",{"WATER":1000,"NACL":1},25,2)],
   [("PRODUCT","OU1"),("WASTE","OU2")]),
 ("OP-ED",  "DBA4D883-276C-4937-82D4-444C5F4D499A",
   [("DILUATEIN","IN1",{"WATER":1000,"NACL":10},25,2),
    ("CONCENTRATEIN","IN2",{"WATER":1000,"NACL":2},25,2)],
   [("DILUATEOUT","OU1"),("CONCENTRATEOUT","OU2")]),
 ("OP-EDI", "59CEAFF7-5F26-406B-9755-A7D7F3BA07A9",
   [("DILUTEIN","IN1",{"WATER":1000,"NACL":0.5},25,2),
    ("CONCENTRATEIN","IN2",{"WATER":1000,"NACL":0.1},25,2)],
   [("DILUTEOUT","OU1"),("CONCENTRATEOUT","OU2")]),
 ("OP-IX",  "4CD0259F-649F-4DB8-90CA-9E78275D3ECF",
   [("FEED","IN1",{"WATER":1000,"NACL":2},25,2),
    ("REGENERANTIN","IN2",{"WATER":200,"NACL":20},25,2)],
   [("TREATED","OU1"),("SPENTOUT","OU2")]),
 ("OP-CHLORALK","DB0C2564-A259-4E82-9038-7C1FD5C1754F",
   [("BRINEIN","IN1",{"WATER":1000,"NACL":300},60,1.5)],
   [("DEPLETEDBRINE","OU1"),("CATHOLYTE","OU2"),("CHLORINE","OU3"),("HYDROGEN","OU4")]),
 ("OP-DLE", "56288974-1BD8-4FA2-B05D-D588A4035F14",
   [("BRINEFEED","IN1",{"WATER":1000,"NACL":100,"LICL":2,"MGCL2":5},25,1.5),
    ("WASHWATER","IN2",{"WATER":100},25,1.5)],
   [("TREATEDBRINE","OU1"),("ELUATE","OU2")]),
 ("OP-SX",  "D51C7832-8220-4A2A-94D7-C3FCC59C5EFA",
   [("AQUEOUSIN","IN1",{"WATER":1000,"LICL":5},25,1.5),
    ("ORGANICIN","IN2",{"TOLUENE":500},25,1.5)],
   [("AQUEOUSOUT","OU1"),("ORGANICOUT","OU2")]),
 ("OP-CRYST","E66A4BC8-E322-419E-BEFD-DDABF14310CC",
   [("FEED","IN1",{"WATER":700,"NACL":300},40,1.01325)],
   [("MOTHERLIQUOR","OU1"),("CRYSTALS","OU2")]),
 ("OP-PPT", "D820BA95-3E24-4E85-81EF-6E8DF1F503C3",
   [("FEED","IN1",{"WATER":1000,"MGCL2":2},25,1.5),
    ("REAGENT","IN2",{"WATER":100,"NA2CO3":5},25,1.5)],
   [("TREATED","OU1"),("SLUDGE","OU2")]),  # soda softening; NB: NAOH as a FEED component crashes this V14 databank (DHVLWT missing) on ANY flowsheet — Aspen-side, see HANDOFF
 ("OP-GAC", "7720A360-E026-4976-8F81-B8DB159F8B8A",
   [("FEED","IN1",{"WATER":1000,"TOLUENE":0.5},25,1.5)],
   [("TREATED","OU1")]),
 ("OP-AEL", "A85DDF54-C98C-465F-9C91-DD94F61E51C3",
   [("WATERFEED","IN1",{"WATER":100},25,1.01325)],
   [("HYDROGEN","OU1"),("OXYGEN","OU2")]),
 ("OP-PEM", "5CF6E601-C4B1-48CD-BE66-54E7500F0626",
   [("WATERFEED","IN1",{"WATER":100},25,1.01325)],
   [("HYDROGEN","OU1"),("OXYGEN","OU2")]),
 ("OP-FC",  "47412507-FBF7-4694-9311-3320F0C7D07D",
   [("HYDROGENIN","IN1",{"H2":2},25,1.5),
    ("AIRIN","IN2",{"O2":20,"N2":66},25,1.5)],
   [("EXHAUST","OU1")]),
 ("OP-RPB", "20916487-6782-448C-85E7-E93E1DB3E632",
   [("GASIN","IN1",{"CO2":50,"N2":500},40,1.1),
    ("LIQUIDIN","IN2",{"WATER":2000},25,1.1)],
   [("GASOUT","OU1"),("LIQUIDOUT","OU2")]),
 ("OP-UVAOP","71AB6CC5-95D4-4C8D-B45F-8623B9E47538",
   [("LIQUIDIN","IN1",{"WATER":1000,"TOLUENE":0.01},25,1.5)],
   [("LIQUIDOUT","OU1")]),
]

# extra components required by validation even if not fed
EXTRA_COMPS = {
    "OP-AEL": ["H2", "O2"], "OP-PEM": ["H2", "O2"], "OP-FC": ["WATER"],
    "OP-CHLORALK": ["NAOH", "CL2", "H2"], "OP-RPB": ["WATER"],
}


def log(m):
    line = time.strftime("%H:%M:%S ") + str(m)
    print(line, flush=True)
    with open(LOG, "a") as f:
        f.write(line + "\n")


def build_inp(code, clsid, inlets, outlets):
    comps = []
    for _, _, flows, _, _ in inlets:
        for c in flows:
            if c not in comps:
                comps.append(c)
    for c in EXTRA_COMPS.get(code, []):
        if c not in comps:
            comps.append(c)
    lines = ["DYNAMICS", "    DYNAMICS RESULTS=ON",
             "IN-UNITS MET PRESSURE=bar TEMPERATURE=C DELTA-T=C PDROP=bar",
             "DEF-STREAMS CONVEN ALL",
             "DATABANKS 'APV140 PURE40' / 'APV140 AQUEOUS' / 'APV140 SOLIDS' &",
             "         / 'APV140 INORGANIC' / 'APESV140 AP-EOS' / 'NISTV140 NIST-TRC' / NOASPENPCD",
             "PROP-SOURCES 'APV140 PURE40' / 'APV140 AQUEOUS' / 'APV140 SOLIDS' &",
             "         / 'APV140 INORGANIC' / 'APESV140 AP-EOS' / 'NISTV140 NIST-TRC'",
             "COMPONENTS"]
    lines.append("    " + " /\n    ".join("%s %s" % (c, COMP_DEFS[c]) for c in comps))
    lines += ["SOLVE", "    RUN-MODE MODE=SIM", "FLOWSHEET",
              "    BLOCK B1 IN=%s OUT=%s" % (" ".join(s for _, s, _, _, _ in inlets),
                                             " ".join(s for _, s in outlets)),
              "PROPERTIES IDEAL"]
    for _, sname, flows, tC, pBar in inlets:
        lines.append("STREAM %s" % sname)
        lines.append("    SUBSTREAM MIXED TEMP=%s PRES=%s" % (tC, pBar))
        lines.append("    MASS-FLOW " + " / ".join("%s %s" % (c, v) for c, v in flows.items()))
    lines += ["BLOCK B1 CAPE-OPEN",
              "    PARAMETERS BYPASS-USER=YES NCHAR=1",
              "    CHAR CHAR-LIST=\"{%s}\"" % clsid,
              "EO-CONV-OPTI", "STREAM-REPOR MOLEFLOW MASSFLOW", ""]
    return "\n".join(lines)


def fresh():
    a = w32.Dispatch("Apwn.Document.40.0")
    a.SuppressDialogs = 1
    try:
        a.Visible = False
    except Exception:
        pass
    return a


def val(a, p):
    n = a.Tree.FindNode(p)
    return None if n is None else n.Value


def decode(bkp):
    try:
        txt = open(bkp, errors="replace").read()
        m = re.search(r'PARAMSTRING\s*=\s*\(\s*(.*?)\)', txt, re.S)
        if not m:
            return None
        b64 = "".join(l.strip().strip('"') for l in m.group(1).splitlines() if l.strip())
        blob = base64.b64decode(b64)
        pos = 4
        tl = blob[pos]; pos += 1; pos += tl; pos += 4
        cnt = struct.unpack_from("<I", blob, pos)[0]; pos += 4
        out = {}
        for _ in range(cnt):
            nl = blob[pos]; pos += 1
            nm = blob[pos:pos+nl].decode(); pos += nl
            k = blob[pos]; pos += 1
            if k == 1:
                v = struct.unpack_from("<d", blob, pos)[0]; pos += 8
                out[nm] = v
            else:
                sl = blob[pos]; pos += 1
                out[nm] = blob[pos:pos+sl].decode(); pos += sl
        return out
    except Exception as e:
        return {"__decode_error__": str(e)}


def run_case(code, clsid, inlets, outlets):
    tag = code.replace("OP-", "").lower()
    inp = os.path.join(OUT, "c_%s.inp" % tag)
    base = os.path.join(OUT, "c_%s_base.apw" % tag)
    bkp = os.path.join(OUT, "c_%s.bkp" % tag)
    with open(inp, "w") as f:
        f.write(build_inp(code, clsid, inlets, outlets))

    a = fresh()
    try:
        a.InitFromFile2(inp)
        a.SaveAs2(base)
        a.Close(); del a; time.sleep(1)

        a = fresh()
        a.InitFromArchive2(base)
        # lazy-init the CAPE-OPEN block, then wire named ports
        _ = a.Tree.FindNode(r"\Data\Blocks\B1\Input\CHAR_LIST").Elements.Item(0).Value
        time.sleep(0.5)
        pnode = a.Tree.FindNode(r"\Data\Blocks\B1\Ports")
        live = [pnode.Elements.Item(i).Name for i in range(pnode.Elements.Count)]
        wiring = [(p, s) for p, s, _, _, _ in inlets] + list(outlets)
        for port, stream in wiring:
            if port not in live:
                return dict(code=code, status="FAIL", why="port %s missing (live: %s)" % (port, live))
            a.Tree.FindNode(r"\Data\Blocks\B1\Ports\%s" % port).Elements.Add(stream)

        a.Reinit(); a.Run2()
        per = val(a, r"\Data\Results Summary\Run-Status\Output\PER_ERROR")
        blk = val(a, r"\Data\Blocks\B1\Output\BLKSTAT")
        msg = val(a, r"\Data\Blocks\B1\Output\BLKMSG")

        mass_in = mass_out = 0.0
        flows = {}
        for _, s, _, _, _ in inlets:
            v = val(a, r"\Data\Streams\%s\Output\MASSFLMX\MIXED" % s) or 0.0
            mass_in += v; flows[s] = v
        for _, s in outlets:
            v = val(a, r"\Data\Streams\%s\Output\MASSFLMX\MIXED" % s) or 0.0
            mass_out += v; flows[s] = v
        a.SaveAs2(bkp)
        a.Close(); del a

        params = decode(bkp) or {}
        bad = [k for k, v in params.items()
               if isinstance(v, float) and (math.isnan(v) or math.isinf(v))]
        mb_rel = abs(mass_in - mass_out) / max(mass_in, 1e-12)
        if code == "OP-UVAOP":
            # advanced-oxidation reactor: the destroyed contaminant is mineralised
            # BY DESIGN (the block warns so). Honest gate: the deficit must equal
            # the fed contaminant (toluene 0.01 kg/h) x reported Destruction %.
            destroyed_kgh = 0.01 * params.get("Destruction", 0.0) / 100.0
            loss_ok = abs((mass_in - mass_out) - destroyed_kgh) < 1e-6 * max(mass_in, 1e-12)
            ok = (blk in (0, 2)) and loss_ok and not bad and mass_out > 0
        elif code == "OP-GAC":
            # fixed-bed adsorber (2 ports, service-cycle model): the captured
            # contaminant stays ON THE BED by design (the block warns so).
            # Honest gate: the deficit must EQUAL the reported adsorbed load
            # (toluene MW 92.141 g/mol), and Aspen may flag warnings (BLKSTAT 2).
            captured_kgh = params.get("AdsorbedLoad", 0.0) * 92.141 / 1000.0 * 3600.0
            holdup_ok = abs((mass_in - mass_out) - captured_kgh) < 1e-6 * max(mass_in, 1e-12)
            ok = (blk in (0, 2)) and holdup_ok and not bad and mass_out > 0
        else:
            ok = (blk == 0) and (mb_rel < 1e-6) and not bad and mass_out > 0
        return dict(code=code, status="PASS" if ok else "FAIL",
                    per=per, blkstat=blk, blkmsg=msg, mb_rel=mb_rel,
                    flows=flows, nonfinite=bad, nparams=len(params),
                    key_outputs={k: round(v, 6) for k, v in list(params.items())
                                 if isinstance(v, float)})
    except Exception as e:
        try:
            a.Close()
        except Exception:
            pass
        return dict(code=code, status="ERROR", why=str(e)[:300])


def main():
    only = set(sys.argv[1:])
    results = []
    for code, clsid, inlets, outlets in CASES:
        if only and code not in only:
            continue
        t0 = time.time()
        r = run_case(code, clsid, inlets, outlets)
        r["secs"] = round(time.time() - t0, 1)
        results.append(r)
        log("%-13s %-5s %5.0fs per=%s blk=%s mb=%s %s" % (
            r["code"], r["status"], r["secs"], r.get("per"), r.get("blkstat"),
            ("%.1e" % r["mb_rel"]) if "mb_rel" in r else "-",
            r.get("why", "") or (r.get("blkmsg") or "")))
        with open(os.path.join(OUT, "sweep25_results.json"), "w") as f:
            json.dump(results, f, indent=1)
    npass = sum(1 for r in results if r["status"] == "PASS")
    log("=== SWEEP DONE: %d/%d PASS ===" % (npass, len(results)))


if __name__ == "__main__":
    main()
