"""Append one MCP entry, validate TOML, preserve all other settings."""
import argparse,datetime,json,sys,tomllib
from pathlib import Path
def main():
 p=argparse.ArgumentParser();p.add_argument('--runtime',required=True);a=p.parse_args()
 root=Path(__file__).parent
 target=Path.home()/'.codex/config.toml';target.parent.mkdir(parents=True,exist_ok=True)
 source=target.read_text(encoding='utf-8-sig') if target.exists() else ''
 obj=tomllib.loads(source)
 expected={'command':a.runtime,'args':[str(root/'client.py'),'--config',str(root/'config.json'),'mcp']}
 old=obj.get('mcp_servers',{}).get('codex-link')
 if old is not None:
  if any(old.get(k)!=v for k,v in expected.items()):raise ValueError('Existing codex-link differs; existing config preserved')
  print('Matching MCP config already registered');return
 block='\n[mcp_servers.codex-link]\ncommand = '+json.dumps(expected['command'])+'\nargs = '+json.dumps(expected['args'])+'\n'
 result=source+block;tomllib.loads(result)
 if target.exists():target.with_name('config.toml.codex-link-backup-'+datetime.datetime.now().strftime('%Y%m%d%H%M%S')).write_text(source,encoding='utf-8')
 target.write_text(result,encoding='utf-8')
 print('MCP config registered; all existing settings preserved. CLI works immediately; new sessions discover MCP.')
if __name__=='__main__':main()
