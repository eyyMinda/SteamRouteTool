// Designer fields are assigned in InitializeComponent, which nullable analysis can't follow.
#nullable disable

namespace SteamRouteTool;

partial class Main
{
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Main));
        this.routeDataGrid = new System.Windows.Forms.DataGridView();
        this.colName = new System.Windows.Forms.DataGridViewTextBoxColumn();
        this.colPing = new System.Windows.Forms.DataGridViewTextBoxColumn();
        this.colBlocked = new System.Windows.Forms.DataGridViewCheckBoxColumn();
        this.btn_PingRoutes = new System.Windows.Forms.Button();
        this.btn_ClearRules = new System.Windows.Forms.Button();
        this.lb_GettingRoutes = new System.Windows.Forms.Label();
        this.btn_About = new System.Windows.Forms.Button();
        ((System.ComponentModel.ISupportInitialize)(this.routeDataGrid)).BeginInit();
        this.SuspendLayout();
        // 
        // routeDataGrid
        // 
        this.routeDataGrid.AllowUserToAddRows = false;
        this.routeDataGrid.AllowUserToDeleteRows = false;
        this.routeDataGrid.AllowUserToResizeColumns = false;
        this.routeDataGrid.AllowUserToResizeRows = false;
        this.routeDataGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        this.routeDataGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
        this.colName,
        this.colPing,
        this.colBlocked});
        this.routeDataGrid.Location = new System.Drawing.Point(1, 1);
        this.routeDataGrid.Name = "routeDataGrid";
        this.routeDataGrid.RowHeadersVisible = false;
        this.routeDataGrid.RowHeadersWidthSizeMode = System.Windows.Forms.DataGridViewRowHeadersWidthSizeMode.DisableResizing;
        this.routeDataGrid.Size = new System.Drawing.Size(269, 240);
        this.routeDataGrid.TabIndex = 0;
        this.routeDataGrid.CellContentClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.RouteDataGrid_CellContentClick);
        // 
        // Name
        // 
        this.colName.HeaderText = "Name";
        this.colName.Name = "colName";
        this.colName.ReadOnly = true;
        this.colName.Resizable = System.Windows.Forms.DataGridViewTriState.False;
        this.colName.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
        this.colName.Width = 155;
        // 
        // Ping
        // 
        this.colPing.HeaderText = "Ping";
        this.colPing.Name = "colPing";
        this.colPing.ReadOnly = true;
        this.colPing.Resizable = System.Windows.Forms.DataGridViewTriState.False;
        this.colPing.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
        this.colPing.Width = 40;
        // 
        // Blocked
        // 
        this.colBlocked.HeaderText = "Blocked";
        this.colBlocked.Name = "colBlocked";
        this.colBlocked.ReadOnly = true;
        this.colBlocked.ThreeState = true;
        this.colBlocked.Resizable = System.Windows.Forms.DataGridViewTriState.False;
        this.colBlocked.Width = 54;
        // 
        // btn_PingRoutes
        // 
        this.btn_PingRoutes.Enabled = false;
        this.btn_PingRoutes.Location = new System.Drawing.Point(196, 241);
        this.btn_PingRoutes.Name = "btn_PingRoutes";
        this.btn_PingRoutes.Size = new System.Drawing.Size(75, 23);
        this.btn_PingRoutes.TabIndex = 1;
        this.btn_PingRoutes.Text = "Ping Routes";
        this.btn_PingRoutes.UseVisualStyleBackColor = true;
        this.btn_PingRoutes.Click += new System.EventHandler(this.Btn_PingRoutes_Click);
        // 
        // btn_ClearRules
        // 
        this.btn_ClearRules.Location = new System.Drawing.Point(0, 241);
        this.btn_ClearRules.Name = "btn_ClearRules";
        this.btn_ClearRules.Size = new System.Drawing.Size(77, 23);
        this.btn_ClearRules.TabIndex = 2;
        this.btn_ClearRules.Text = "Clear Rules";
        this.btn_ClearRules.UseVisualStyleBackColor = true;
        this.btn_ClearRules.Click += new System.EventHandler(this.Btn_ClearRules_Click);
        // 
        // lb_GettingRoutes
        // 
        this.lb_GettingRoutes.AutoSize = true;
        this.lb_GettingRoutes.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
        this.lb_GettingRoutes.ForeColor = System.Drawing.Color.Red;
        this.lb_GettingRoutes.Location = new System.Drawing.Point(98, 246);
        this.lb_GettingRoutes.Name = "lb_GettingRoutes";
        this.lb_GettingRoutes.Size = new System.Drawing.Size(82, 13);
        this.lb_GettingRoutes.TabIndex = 3;
        this.lb_GettingRoutes.Text = "Getting routes...";
        // 
        // btn_About
        // 
        this.btn_About.Location = new System.Drawing.Point(111, 241);
        this.btn_About.Name = "btn_About";
        this.btn_About.Size = new System.Drawing.Size(51, 23);
        this.btn_About.TabIndex = 4;
        this.btn_About.Text = "About";
        this.btn_About.UseVisualStyleBackColor = true;
        this.btn_About.Visible = false;
        this.btn_About.Click += new System.EventHandler(this.Btn_About_Click);
        // 
        // Main
        // 
        this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(271, 264);
        this.Controls.Add(this.btn_About);
        this.Controls.Add(this.lb_GettingRoutes);
        this.Controls.Add(this.btn_ClearRules);
        this.Controls.Add(this.btn_PingRoutes);
        this.Controls.Add(this.routeDataGrid);
        this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
        this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
        this.MaximizeBox = false;
        this.Text = "Steam Route Tool";
        ((System.ComponentModel.ISupportInitialize)(this.routeDataGrid)).EndInit();
        this.ResumeLayout(false);
        this.PerformLayout();

    }

    #endregion

    private System.Windows.Forms.DataGridView routeDataGrid;
    private System.Windows.Forms.Button btn_PingRoutes;
    private System.Windows.Forms.Button btn_ClearRules;
    private System.Windows.Forms.Label lb_GettingRoutes;
    private System.Windows.Forms.DataGridViewTextBoxColumn colName;
    private System.Windows.Forms.DataGridViewTextBoxColumn colPing;
    private System.Windows.Forms.DataGridViewCheckBoxColumn colBlocked;
    private System.Windows.Forms.Button btn_About;
}
