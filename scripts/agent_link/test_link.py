import concurrent.futures,contextlib,json,os,sqlite3,subprocess,sys,tempfile,unittest
from pathlib import Path
from server import Mailbox,safe
from client import Client
class Tests(unittest.TestCase):
 def setUp(self):self.temp=tempfile.TemporaryDirectory(dir=os.environ.get('CLIPSHOT_LINK_TEST_ROOT'));self.db=Path(self.temp.name)/'mail.sqlite3';self.work=Mailbox(self.db,'work');self.dacha=Mailbox(self.db,'dacha')
 def tearDown(self):self.temp.cleanup()
 def send(self,**kw):return self.work.call('link_send',dict(to='dacha',thread='test',kind='task',body='Synthetic test',key='id-1')|kw)
 def test_persistence_and_receipt(self):
  seq=self.send()['seq'];other=Mailbox(self.db,'dacha');self.assertEqual(other.call('link_inbox',{})['messages'][0]['seq'],seq)
  other.call('link_ack',{'seq':seq,'state':'received'});self.assertEqual(len(other.call('link_inbox',{})['messages']),1)
  other.call('link_ack',{'seq':seq,'state':'done','evidence':'synthetic checked'});self.assertEqual(other.call('link_inbox',{})['messages'],[])
 def test_idempotency_conflict(self):
  seq=self.send()['seq'];self.assertEqual(self.send()['seq'],seq)
  with self.assertRaises(ValueError):self.send(body='Changed')
 def test_parallel_same_key_one_record(self):
  with concurrent.futures.ThreadPoolExecutor(max_workers=8) as e:values=list(e.map(lambda _:Mailbox(self.db,'work').call('link_send',dict(to='dacha',thread='test',kind='test',body='Synthetic',key='parallel')),range(16)))
  self.assertEqual(len(set(x['seq'] for x in values)),1)
 def test_recipient_only(self):
  seq=self.send()['seq']
  with self.assertRaises(ValueError):self.work.call('link_ack',{'seq':seq,'state':'done','evidence':'fake'})
 def test_done_requires_evidence(self):
  seq=self.send()['seq']
  with self.assertRaises(ValueError):self.dacha.call('link_ack',{'seq':seq,'state':'done'})
 def test_reply_same_thread(self):
  seq=self.send()['seq']
  with self.assertRaises(ValueError):self.dacha.call('link_send',dict(to='work',thread='other',kind='result',body='Synthetic',key='r1',reply_to=seq))
 def test_claim_once_and_only_worker(self):
  seq=self.send(to='deepseek')['seq'];ds=Mailbox(self.db,'deepseek')
  self.assertTrue(ds.call('link_claim',{'seq':seq})['claimed']);self.assertFalse(ds.call('link_claim',{'seq':seq})['claimed'])
  with self.assertRaises(ValueError):self.work.call('link_claim',{'seq':seq})
 def test_secret_filter(self):
  for value in ['password=not-a-real-password','Bearer abcdefghijklmno','-----BEGIN OPENSSH PRIVATE KEY-----']:
   with self.assertRaises(ValueError):safe(value)
 def test_presence_expiration(self):
  self.dacha.call('link_presence',{'state':'available','ttl':30})
  with contextlib.closing(sqlite3.connect(self.db)) as db:db.execute('UPDATE presence SET expires=0');db.commit()
  self.assertFalse(self.work.call('link_info',{})['presence'][0]['fresh'])
 def test_bounds_and_unknown(self):
  for tool,args in [('link_inbox',{'limit':500}),('link_send',{'shell':'whoami'}),('exec',{})]:
   with self.assertRaises(ValueError):self.work.call(tool,args)
 def test_local_mcp_wire(self):
  req=[{'jsonrpc':'2.0','id':1,'method':'initialize','params':{'protocolVersion':'2025-03-26'}},{'jsonrpc':'2.0','id':2,'method':'tools/list'},{'jsonrpc':'2.0','id':3,'method':'tools/call','params':{'name':'link_info','arguments':{}}}]
  p=subprocess.run([sys.executable,str(Path(__file__).with_name('server.py')),'--db',str(self.db),'--actor','work'],input=('\n'.join(map(json.dumps,req))+'\n').encode(),stdout=subprocess.PIPE,check=True)
  rows=[json.loads(s) for s in p.stdout.splitlines()];self.assertEqual(len(rows[1]['result']['tools']),7);self.assertFalse(rows[2]['result']['isError'])
 def test_outbox_survives_offline_then_flushes_once(self):
  path=Path(self.temp.name)/'config.json';path.write_text(json.dumps({'actor':'work'}));client=Client(path)
  args=dict(to='dacha',thread='test',kind='task',body='Synthetic offline',key='offline1')
  def offline(*args):raise OSError('offline')
  client.call=offline
  with self.assertRaises(OSError):client.send(args)
  second=Client(path);second.call=lambda name,args,actor=None:self.work.call(name,args)
  result=second.flush();self.assertEqual(len(result),1);self.assertEqual(second.flush(),[])
  self.assertEqual(len(self.dacha.call('link_inbox',{})['messages']),1)
  self.assertEqual(second.send(args)['seq'],result[0]['seq'])
if __name__=='__main__':unittest.main()
