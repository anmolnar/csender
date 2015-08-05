using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.ServiceProcess;
using System.Text;
using System.Data.SqlClient;
using System.IO;
using System.Timers;
using System.Net.Mail;
using System.Threading;
using System.Management;
using System.Text.RegularExpressions;
using System.Net;

namespace CSender
{
  public partial class CSender : ServiceBase
  {
    private string server;
    private string sqlstr;

    private string confsmtp;
    private string confsmtpuser;
    private string confsmtppass;
    private string confver;
    private string senders;

    private System.Timers.Timer t;

    private Int16 state = 0;

    //private ArrayList embeddedimgs;
    private Dictionary<string, string> embeddedimgs;

    public CSender()
    {
      InitializeComponent();
    }

    protected override void OnStart(string[] args)
    {
      bool sleep_req = false;
      string line;
      string sqldb = "";
      string sqluser = "";
      string sqlpass = "";

      // TODO: Add code here to start your service.
      try
      {
        try
        {
          StreamReader inifile = new StreamReader(System.Environment.CurrentDirectory + "\\mailed.ini", new System.Text.UTF8Encoding());
          line = inifile.ReadLine();
          server = line.Substring(10);
          line = inifile.ReadLine();
          sqldb = line.Substring(6);
          line = inifile.ReadLine();
          sqluser = line.Substring(8);
          line = inifile.ReadLine();
          sqlpass = line.Substring(8);
          line = inifile.ReadLine();
          confsmtp = line.Substring(5);
          line = inifile.ReadLine();
          confsmtpuser = line.Substring(9);
          line = inifile.ReadLine();
          confsmtppass = line.Substring(9);
          line = inifile.ReadLine();
          confver = line.Substring(8);
          line = inifile.ReadLine();
          senders = line.Substring(8);
          inifile.Close();
          inifile.Dispose();
        }
        catch (Exception ex)
        {
          EventLog.WriteEntry("Ini read error: ", ex.ToString(), EventLogEntryType.Error);
        }

        sqlstr = "data source = " + server + "; initial catalog = " + sqldb + "; user id = " + sqluser + "; password = " + sqlpass;
        do
        {
          if (sleep_req)
          {
            System.Threading.Thread.Sleep(15 * 1000);
            sleep_req = false;
          }
          try
          {
            SqlConnection sqlcon = new SqlConnection(sqlstr);
            sqlcon.Open();
            sqlcon.Close();
            sqlcon.Dispose();
          }
          catch (SqlException sqle)
          {
            EventLog.WriteEntry("SQL server error: " + sqle.Message.ToString().Substring(0, 50) + "...", "OnStart: " + sqle.Message.ToString(), EventLogEntryType.Warning);
            EventLog.WriteEntry("Sleeping for 15 seconds...", "OnStart");
            sleep_req = true;
          }

        } while (sleep_req);

        SendMsg(string.Format("Connected to SQL server: {0}", sqlstr));

        t = new System.Timers.Timer(1000);
        t.Elapsed += new ElapsedEventHandler(t_Elapsed);
        t.AutoReset = true;
        t.Start();
      }
      catch (Exception e)
      {
        EventLog.WriteEntry("Service start error: ", e.ToString(), EventLogEntryType.Error);
      }
    }

    void t_Elapsed(object sender, ElapsedEventArgs e)
    {
      //throw new Exception("The method or operation is not implemented.");
      try
      {
        t.Stop();

        Thread thr = new Thread(SendEmails);
        thr.IsBackground = true;
        thr.Start();
        thr.Join();

        //SendEmails();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        t.Start();
      }
      catch (Exception the)
      {
        ReportError(the);
      }
    }

    private void SendEmails()
    {
      try
      {
        using (SqlConnection sqlcon = new SqlConnection(sqlstr))
        {
          SqlCommand sqlcom = new SqlCommand(sqlstr, sqlcon);
          SqlDataAdapter sqldata = new SqlDataAdapter();
          DataSet ds = new DataSet();
          sqldata.SelectCommand = sqlcom;

          sqlcom.CommandText = "select top 1 sndbuff_snd_id, user_topsending from sending_buffer "
                      + " left join newsletter on sndbuff_nl_id=nl_id left join campaigns on nl_cam_id=cam_id "
                      + " left join users on cam_user_id=user_id "
                      + " where sndbuff_time<getdate() and sndbuff_sent is null order by sndbuff_time";

          if (ds.Tables.Contains("snd_top")) { ds.Tables["snd_top"].Clear(); }
          sqldata.Fill(ds, "snd_top");

          string mailedroot = "";

          if (ds.Tables["snd_top"].Rows.Count > 0)
          {

            sqlcom.CommandText = "select admin_mailedroot, admin_mailedtxtroot from admins where admin_id=1";
            if (ds.Tables.Contains("settings")) { ds.Tables["settings"].Clear(); }
            sqldata.Fill(ds, "settings");

            mailedroot = ds.Tables["settings"].Rows[0]["admin_mailedroot"].ToString();

            sqlcom.CommandText = "select top " + ds.Tables["snd_top"].Rows[0]["user_topsending"].ToString()
                    + " sndbuff_nl_id, sndbuff_id, sndbuff_snd_id, sndbuff_from, sndbuff_fromname, sndbuff_email, sndbuff_nl_subject, sndbuff_mem_id, "
                    + " sndbuff_mode, cam_user_id, cam_id, sndbuff_time from sending_buffer "
                    + " left join newsletter on sndbuff_nl_id=nl_id left join campaigns on nl_cam_id=cam_id "
                    + " where sndbuff_time<getdate() and sndbuff_sent is null order by sndbuff_time";

            if (ds.Tables.Contains("sendnow")) { ds.Tables["sendnow"].Clear(); }
            sqldata.Fill(ds, "sendnow");

            ArrayList attaches = new ArrayList();
            bool sleep_req = false;

            int snd = 0;

            for (int i = 0; i < ds.Tables["sendnow"].Rows.Count; i++)
            {
              sqlcom.CommandText = "select att_filename from attached left join attachment on nlatt_att_id = att_id where nlatt_nl_id= " + ds.Tables["sendnow"].Rows[i]["sndbuff_nl_id"].ToString();
              if (ds.Tables.Contains("attaches")) { ds.Tables["attaches"].Clear(); }
              sqldata.Fill(ds, "attaches");

              for (int j = 0; j < ds.Tables["attaches"].Rows.Count; j++)
              {
                attaches.Add(ds.Tables["attaches"].Rows[j]["att_filename"].ToString());
              }

              string nlbody = "";
              string nltextbody = "";

              int ret = 0;

              do
              {
                if (sleep_req)
                {
                  System.Threading.Thread.Sleep(1 * 1000);
                  sleep_req = false;
                  ret++;
                }
                try
                {
                  //ha volt hiba, akkor nincs mar ien txt
                  if (File.Exists(ds.Tables["settings"].Rows[0]["admin_mailedtxtroot"].ToString() + "\\" + ds.Tables["sendnow"].Rows[i]["sndbuff_id"].ToString() + ".txt"))
                  {
                    FileStream fs = new FileStream(ds.Tables["settings"].Rows[0]["admin_mailedtxtroot"].ToString() + "\\" + ds.Tables["sendnow"].Rows[i]["sndbuff_id"].ToString() + ".txt", FileMode.Open, FileAccess.Read, FileShare.None);
                    FileStream fst = new FileStream(ds.Tables["settings"].Rows[0]["admin_mailedtxtroot"].ToString() + "\\" + ds.Tables["sendnow"].Rows[i]["sndbuff_id"].ToString() + "_txt.txt", FileMode.Open, FileAccess.Read, FileShare.None);

                    StreamReader s = new StreamReader(fs, System.Text.Encoding.UTF8);
                    StreamReader st = new StreamReader(fst, System.Text.Encoding.UTF8);

                    s.BaseStream.Seek(0, SeekOrigin.Begin);
                    st.BaseStream.Seek(0, SeekOrigin.Begin);

                    nlbody = s.ReadToEnd();
                    nltextbody = st.ReadToEnd();

                    s.Close();
                    st.Close();

                    s.Dispose();
                    st.Dispose();

                    fs.Dispose();
                    fst.Dispose();
                  }
                  else
                  {
                    //ha nem letezik akkor megy tovabb
                    sleep_req = false;
                  }
                }
                catch (IOException ioe)
                {
                  SendMsg(ioe);
                  sleep_req = true;
                }

              } while (sleep_req && 5 < ret);

              //ha nincs levelbody, nem kuldunk levelet...ha van kuldunk, es varjuk hogy sikerult e, ha nem ujra probalkozunk
              //siker csak kikuldes, vagy hibas emailcim eseten van, hiba pedig io ex eseten
              if ((nlbody != "") || (nltextbody != ""))
              {
                if (snd != Convert.ToInt32(ds.Tables["sendnow"].Rows[i]["sndbuff_snd_id"]))
                {
                  snd = Convert.ToInt32(ds.Tables["sendnow"].Rows[i]["sndbuff_snd_id"]);
                  Regex re = new Regex("<img[^>]*(?:src=\"([^\"]*)[^>]*embedded=\"true\"|embedded=\"true\"[^>]*src=\"([^\"]*))[^>]*>");

                  //kiszedni kepeket, es letolteni
                  if (re.IsMatch(nlbody))
                  {
                    //embeddedimgs = new ArrayList();
                    embeddedimgs = new Dictionary<string, string>();
                    string filename;
                    // mailedroot = ds.Tables["settings"].Rows[0]["admin_mailedroot"].ToString();
                    foreach (Match tmp in re.Matches(nlbody))
                    {
                      string img = (tmp.Groups[1].Value != "") ? tmp.Groups[1].Value : tmp.Groups[2].Value;
                      if (!embeddedimgs.ContainsKey(img))
                      {
                        filename = img.Replace(":", "").Replace("/", "").Replace(".", "").Replace("?", "").Replace("&", "").Replace("=", "");
                        embeddedimgs.Add(img, filename);
                        //tarolni                                                
                        if (!Directory.Exists(mailedroot + "upload\\embedded\\" + snd.ToString()))
                        {
                          Directory.CreateDirectory(mailedroot + "upload\\embedded\\" + snd.ToString());
                        }
                        if (!File.Exists(mailedroot + "upload\\embedded\\" + snd.ToString() + "\\" + filename + ".jpg"))
                        {
                          //letoltom es elmentem
                          WriteBytesToFile(mailedroot + "upload\\embedded\\" + snd.ToString() + "\\" + filename + ".jpg", GetBytesFromUrl(img));
                        }
                      }
                    }
                  }
                  else
                  {
                    embeddedimgs = null;
                  }
                }

                if (!messenger(ds.Tables["sendnow"].Rows[i]["sndbuff_from"].ToString(), ds.Tables["sendnow"].Rows[i]["sndbuff_fromname"].ToString(), ds.Tables["sendnow"].Rows[i]["sndbuff_email"].ToString(), ds.Tables["sendnow"].Rows[i]["sndbuff_nl_subject"].ToString(), nlbody, nltextbody, attaches, Convert.ToInt16(ds.Tables["sendnow"].Rows[i]["sndbuff_mode"]), Convert.ToInt32(ds.Tables["sendnow"].Rows[i]["cam_user_id"]), Convert.ToInt32(ds.Tables["sendnow"].Rows[i]["sndbuff_mem_id"]), Convert.ToInt32(ds.Tables["sendnow"].Rows[i]["cam_id"]), ds.Tables["settings"].Rows[0]["admin_mailedroot"].ToString(), snd))
                {
                  i--;
                }
                nlbody = "";
                nltextbody = "";
              }

              if (attaches.Count > 0) { attaches.Clear(); }

              try
              {
                sqlcom.CommandText = "update sending_buffer set sndbuff_sent=1 where sndbuff_id=" + ds.Tables["sendnow"].Rows[i]["sndbuff_id"].ToString();
                sqlcon.Open();
                sqlcom.ExecuteNonQuery();
                sqlcon.Close();

                if (File.Exists(ds.Tables["settings"].Rows[0]["admin_mailedtxtroot"].ToString() + "\\" + ds.Tables["sendnow"].Rows[i]["sndbuff_id"].ToString() + ".txt"))
                {
                  File.Delete(ds.Tables["settings"].Rows[0]["admin_mailedtxtroot"].ToString() + "\\" + ds.Tables["sendnow"].Rows[i]["sndbuff_id"].ToString() + ".txt");
                  File.Delete(ds.Tables["settings"].Rows[0]["admin_mailedtxtroot"].ToString() + "\\" + ds.Tables["sendnow"].Rows[i]["sndbuff_id"].ToString() + "_txt.txt");
                }

              }
              catch (Exception updtex)
              {
                ReportError(updtex);
              }
            }
            attaches.Clear();
          }

          //ide jon a torles ha vegzett a kikuldes
          sqlcom.CommandText = " select count(snd_id) as num, " +
                              " (select count(sndbuff_id) from sending_buffer where sndbuff_sent is null) as inbuff, " +
                              " (select count(sndbuff_id) from sending_buffer where sndbuff_sent is not null) as inbuffsent  " +
                              " from sending where snd_sent<>1 ";
          if (ds.Tables.Contains("checkfordel")) { ds.Tables["checkfordel"].Clear(); }
          sqldata.Fill(ds, "checkfordel");

          if ((Convert.ToInt32(ds.Tables["checkfordel"].Rows[0]["num"]) == 0) && (Convert.ToInt32(ds.Tables["checkfordel"].Rows[0]["inbuff"]) == 0) && (Convert.ToInt32(ds.Tables["checkfordel"].Rows[0]["inbuffsent"]) != 0))
          {
            sqlcom.CommandText = "DELETE from sending_buffer ";
            sqlcom.CommandTimeout = 1000;
            sqlcon.Open();
            sqlcom.ExecuteNonQuery();
            sqlcon.Close();

            //az embedded imgs konyvarak torlese!
            try
            {
              string[] directories = Directory.GetDirectories(mailedroot + "upload\\embedded\\");
              foreach (string directory in directories)
              {
                Array.ForEach(Directory.GetFiles(@mailedroot + "upload\\embedded\\", "*.jpg", SearchOption.AllDirectories), delegate(string path) { File.Delete(path); });
                Directory.Delete(directory);
              }
            }
            catch { }
          }

          ds.Tables.Clear();

          sqldata.Dispose();
          sqlcom.Dispose();
          sqlcon.Dispose();
          ds.Dispose();
        }
      }
      catch (Exception sendex)
      {
        ReportError(sendex);
      }
      finally
      {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
      }
    }

    private void CheckFreeSpace(string drive)
    {
      bool sleep_req = false;
      do
      {
        if (sleep_req)
        {
          System.Threading.Thread.Sleep(15 * 1000);
          sleep_req = false;
        }
        try
        {
          ManagementObject disk = new ManagementObject("win32_logicaldisk.deviceid=\"" + drive + "\"");
          disk.Get();
          if (Convert.ToInt64(disk["FreeSpace"]) < 5242880)
          {
            SendMsg(disk["FreeSpace"].ToString());
            throw new ManagementException();
          }
          disk.Dispose();
        }
        catch (ManagementException mex)
        {
          EventLog.WriteEntry("Out of disk space error: ", "CCreator Creator: " + mex.ToString(), EventLogEntryType.Warning);
          EventLog.WriteEntry("Sleeping for 15 seconds...", "CCreator Creator");
          sleep_req = true;
        }
      } while (sleep_req);

      GC.Collect();
      GC.WaitForPendingFinalizers();
      GC.Collect();
    }

    private bool messenger(string emailfrom, string emailfromname, string emailto, string subject, string body, string txtbody, ArrayList attachments, Int16 sendmode, Int32 user_id, Int32 mem_id, Int32 cam_id, string mailedroot, int snd_id)
    {
      try
      {
        using (MailMessage mail = new MailMessage())
        {
          if (emailfromname != "")
          {
            mail.From = new MailAddress(emailfrom, emailfromname, System.Text.Encoding.UTF8);
          }
          else
          {
            mail.From = new MailAddress(emailfrom);
          }
          mail.To.Add(emailto.Trim());
          mail.Subject = subject;
          mail.SubjectEncoding = System.Text.Encoding.UTF8;
          mail.BodyEncoding = System.Text.Encoding.UTF8;

          if (sendmode == 1)
          {
            //kicserélem a bodyban a képeket cidre

            if (embeddedimgs != null)
            {
              //van beágyazott kép, kicseréljuk a bodyban
              foreach (KeyValuePair<string, string> kvp in embeddedimgs)
              {
                body = body.Replace(kvp.Key, "cid:" + kvp.Value);
              }
            }

            AlternateView althtml = AlternateView.CreateAlternateViewFromString(body, System.Text.Encoding.UTF8, System.Net.Mime.MediaTypeNames.Text.Html);
            althtml.TransferEncoding = System.Net.Mime.TransferEncoding.Base64;

            if (embeddedimgs != null)
            {
              //van beágyazott kép, hozzárakjuk a mailhez
              foreach (KeyValuePair<string, string> kvp in embeddedimgs)
              {
                LinkedResource embimg = new LinkedResource(mailedroot + "upload\\embedded\\" + snd_id.ToString() + "\\" + kvp.Value + ".jpg", "image/jpeg");
                embimg.ContentId = kvp.Value;
                althtml.LinkedResources.Add(embimg);
              }
            }

            mail.AlternateViews.Add(althtml);
          }
          if (sendmode == 2)
          {
            AlternateView alttext = AlternateView.CreateAlternateViewFromString(txtbody.Replace("<br>", "\r\n"), System.Text.Encoding.UTF8, System.Net.Mime.MediaTypeNames.Text.Plain);
            alttext.TransferEncoding = System.Net.Mime.TransferEncoding.Base64;
            mail.AlternateViews.Add(alttext);
            /*mail.IsBodyHtml = false;
            mail.Body = txtbody.Replace("<br>", "\r\n");
             * */
          }
          if (sendmode == 3)
          {
            if (embeddedimgs != null)
            {
              //van beágyazott kép, kicseréljuk a bodyban
              foreach (KeyValuePair<string, string> kvp in embeddedimgs)
              {
                body = body.Replace(kvp.Key, "cid:" + kvp.Value);
              }
            }

            AlternateView alttext = AlternateView.CreateAlternateViewFromString(txtbody.Replace("<br>", "\r\n"), System.Text.Encoding.UTF8, System.Net.Mime.MediaTypeNames.Text.Plain);
            AlternateView althtml = AlternateView.CreateAlternateViewFromString(body, System.Text.Encoding.UTF8, System.Net.Mime.MediaTypeNames.Text.Html);

            althtml.TransferEncoding = System.Net.Mime.TransferEncoding.Base64;
            alttext.TransferEncoding = System.Net.Mime.TransferEncoding.Base64;

            if (embeddedimgs != null)
            {
              //van beágyazott kép, hozzárakjuk a mailhez
              foreach (KeyValuePair<string, string> kvp in embeddedimgs)
              {
                LinkedResource embimg = new LinkedResource(mailedroot + "upload\\embedded\\" + snd_id.ToString() + "\\" + kvp.Value + ".jpg", "image/jpeg");
                embimg.ContentId = kvp.Value;
                althtml.LinkedResources.Add(embimg);
              }
            }

            mail.AlternateViews.Add(alttext);
            mail.AlternateViews.Add(althtml);
          }

          foreach (string tmp in attachments)
          {
            if (tmp != "")
            {
              Attachment msgattach = new Attachment(mailedroot + "upload\\attachments\\" + cam_id.ToString() + "\\" + tmp);
              mail.Attachments.Add(msgattach);
            }
          }

          //     mail.Headers.Add("X-Mailer-Engine", "FOLTnet Newsletter Service");
          //     mail.Headers.Add("Organization", "FOLTnet Kft. (c) 2002-2008; http://www.foltnet.hu");
          mail.Headers.Add("Precedence", "list");
          mail.Headers.Add("importance", "normal");

          bool sent = false;

          SmtpClient smtp = new SmtpClient();
          Array smpts = senders.Split(new Char[] { '+' });
          for (int i = 0; i < smpts.Length; i++)
          {
            if (state == smpts.Length)
            {
              state = 0;
            }
            if ((state == i) && (!sent))
            {
              Array sm = smpts.GetValue(i).ToString().Split(new Char[] { '-' });
              smtp.Host = sm.GetValue(0).ToString();
              if (sm.Length > 0)
              {
                smtp.Credentials = new System.Net.NetworkCredential(sm.GetValue(1).ToString(), sm.GetValue(2).ToString());
              }
              sent = true;
              state++;
            }
          }

          //smtp.Timeout = 20000;
          smtp.Send(mail);
          mail.Dispose();
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return true;
      }
      catch (FormatException messex)
      {
        //ha nem megfelelo email cim, akkor megy a szemetbe
        SendMsg(messex);
        using (SqlConnection sqlcon = new SqlConnection(sqlstr))
        {
          try
          {
            SqlCommand sqlcom = new SqlCommand(sqlstr, sqlcon);
            sqlcom.CommandText = "exec bad_emailer @email, @id, @mem_id, @desc";
            sqlcom.Parameters.Clear();
            sqlcom.Parameters.Add("@id", SqlDbType.BigInt).Value = user_id;
            sqlcom.Parameters.Add("@email", SqlDbType.NVarChar).Value = emailto;
            sqlcom.Parameters.Add("@mem_id", SqlDbType.BigInt).Value = mem_id;
            sqlcom.Parameters.Add("@desc", SqlDbType.Int).Value = 1;

            sqlcon.Open();
            sqlcom.ExecuteNonQuery();
            sqlcon.Close();

            sqlcom.Dispose();
            sqlcon.Dispose();
          }
          catch (Exception exex)
          {
            ReportError(exex);
          }
          return true;
        }
      }
      catch (System.Net.Mail.SmtpException smex)
      {
        //ha nem megfelelo email cim, akkor megy a szemetbe
        SendMsg(smex);
        using (SqlConnection sqlcon = new SqlConnection(sqlstr))
        {
          try
          {
            SqlCommand sqlcom = new SqlCommand(sqlstr, sqlcon);
            sqlcom.CommandText = "exec bad_emailer @email, @id, @mem_id, @desc";
            sqlcom.Parameters.Clear();
            sqlcom.Parameters.Add("@id", SqlDbType.BigInt).Value = user_id;
            sqlcom.Parameters.Add("@email", SqlDbType.NVarChar).Value = emailto;
            sqlcom.Parameters.Add("@mem_id", SqlDbType.BigInt).Value = mem_id;
            sqlcom.Parameters.Add("@desc", SqlDbType.Int).Value = 2;

            sqlcon.Open();
            sqlcom.ExecuteNonQuery();
            sqlcon.Close();

            sqlcom.Dispose();
            sqlcon.Dispose();
          }
          catch (Exception exexx)
          {
            ReportError(exexx);
          }
          return true;
        }
      }
      catch (System.IO.IOException ioex)
      {
        //ha io hiba miatt akadt ki valszeg nincs hely, checkkingg
        CheckFreeSpace("c:");
        ReportError(ioex);
        return false;
      }
      catch (Exception e)
      {
        //barmi mas hiba
        ReportError(e);
        return true;
      }
    }

    protected override void OnStop()
    {
      // TODO: Add code here to perform any tear-down necessary to stop your service.
      t.Stop();
      t.Dispose();
      GC.Collect();
      GC.WaitForPendingFinalizers();
      GC.Collect();
    }

    private void ReportError(Exception e_)
    {
      EventLog.WriteEntry(string.Format("Error in CSender: {0}", e_.ToString()), EventLogEntryType.Error);
    }

    private void SendMsg(Exception e_)
    {
      EventLog.WriteEntry(string.Format("Error in CSender: {0}", e_.ToString()), EventLogEntryType.Error);
    }

    private void SendMsg(string message_)
    {
      EventLog.WriteEntry(string.Format("Message from CSender: {0}", message_), EventLogEntryType.Warning);
    }

    private byte[] GetBytesFromUrl(string url)
    {
      byte[] b;
      HttpWebRequest myReq = (HttpWebRequest)WebRequest.Create(url);
      WebResponse myResp = myReq.GetResponse();
      Stream stream = myResp.GetResponseStream();
      using (BinaryReader br = new BinaryReader(stream))
      {
        //i = (int)(stream.Length);
        b = br.ReadBytes(500000);
        br.Close();
      }
      myResp.Close();
      return b;
    }

    private void WriteBytesToFile(string fileName, byte[] content)
    {
      FileStream fs = new FileStream(fileName, FileMode.Create);
      BinaryWriter w = new BinaryWriter(fs);
      try
      {
        w.Write(content);
      }
      finally
      {
        fs.Close();
        w.Close();
      }
    }
  }
}
