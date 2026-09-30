// Local HTML review QA; run after Play performance measurements have ended.
const {chromium}=require('playwright');
const fs=require('fs'),path=require('path'),assert=require('assert/strict');
const {pathToFileURL}=require('url');
const out=path.resolve(__dirname,'../../Art/World/WorldMacro/Compact/InkLandscape/BroadBrush');
(async()=>{
 const result={status:'RUNNING',views:[],layouts:[],errors:[]};let browser;
 try{
  const views=JSON.parse(fs.readFileSync(path.join(out,'views.json'),'utf8').replace(/^\uFEFF/,'' )).views;
  browser=await chromium.launch({channel:'msedge',headless:true});
  const page=await browser.newPage({viewport:{width:1600,height:1100}});
  page.on('pageerror',e=>result.errors.push(e.message));
  page.on('console',m=>{if(m.type()==='error')result.errors.push(m.text());});
  await page.goto(pathToFileURL(path.join(out,'REVIEW.html')).href);
  async function ready(id,name){
   await page.waitForFunction(({id,name})=>{const e=document.getElementById(id);return e.complete&&e.naturalWidth===1920&&e.naturalHeight===1080&&new URL(e.src).pathname.endsWith('/'+name);},{id,name});
   await page.locator('#'+id).evaluate(e=>e.decode());
  }
  assert.equal(await page.locator('#stage').inputValue(),'after');
  assert.equal(await page.locator('#view option').count(),15);
  for(const v of views){
   await page.selectOption('#view',v.id);
   for(const side of ['before','after']){
    await page.selectOption('#stage',side);await ready('large',`${side}_${v.id}.png`);
    assert.equal(await page.locator('#largeLink').getAttribute('href'),`${side}_${v.id}.png`);
   }
   await page.click('#mode');
   assert.equal(await page.locator('#comparison').isVisible(),true);
   await ready('before',`before_${v.id}.png`);await ready('after',`after_${v.id}.png`);
   assert.equal(await page.locator('#imageError').isVisible(),false);
   await page.click('#mode');result.views.push(v.id);
  }
  for(const width of [1600,390]){
   await page.setViewportSize({width,height:1100});
   for(const pair of [false,true]){
    if(pair)await page.click('#mode');
    const size=await page.evaluate(()=>({document:document.documentElement.scrollWidth,client:document.documentElement.clientWidth}));
    assert.ok(size.document<=size.client+1);result.layouts.push({width,pair,...size});
    if(pair)await page.click('#mode');
   }
  }
  await page.setViewportSize({width:1600,height:1100});await page.selectOption('#view','inn');await page.selectOption('#stage','after');await ready('large','after_inn.png');
  await page.screenshot({path:path.join(out,'review_layout.png')});
  await page.click('#toggle');await ready('large','before_inn.png');await page.click('#toggle');await ready('large','after_inn.png');
  await page.click('#next');assert.equal(await page.locator('#view').inputValue(),'mountain_path');await page.click('#previous');assert.equal(await page.locator('#view').inputValue(),'inn');
  assert.deepEqual(result.errors,[]);result.status='PASS';
 }catch(e){result.status='FAIL';result.failure=e.stack;process.exitCode=1;}
 finally{if(browser)await browser.close();fs.writeFileSync(path.join(out,'review_page_checks.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result));}
})();
